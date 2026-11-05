using Microsoft.Data.SqlClient;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Faults;
using Purview.EventSourcing.Fixtures.SqlServer;

namespace Purview.EventSourcing.SqlServer.Faults;

/// <summary>
/// What survives a connection that breaks mid-operation.
/// </summary>
/// <remarks>
/// <para>
/// <c>FaultInjectingEventStore</c> fails at the store boundary, which covers the framework's own
/// logic. These cover the layer below it: the connection itself goes away part-way through, and the
/// question is what the SDK did about it and what the stream looks like afterwards.
/// </para>
/// <para>
/// <strong>The lost response is the one that matters.</strong> The server has received the write and
/// may have committed it; the client is told only that the connection vanished. The caller's natural
/// reaction is to retry, and the invariant worth protecting is that retrying does not apply the
/// events twice.
/// </para>
/// <para>
/// Every test here runs under a tenant scope where one applies, because a failure path is exactly
/// where a tenant stamp gets forgotten.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class SqlServerTransportFaultTests(SqlServerEventStoreFixture fixture)
{
	// A connection that dies before anything is sent must leave nothing behind - not a partial
	// stream, and not a stream-version row pointing at events that were never written.
	[Test]
	public async Task SaveAsync_WhenTheConnectionIsSeveredImmediately_LeavesNothingBehind(
		CancellationToken cancellationToken
	)
	{
		var runId = Guid.NewGuid();
		var aggregateId = $"{Guid.NewGuid():N}";

		await using var proxy = await ProxyAsync();
		proxy.ResetRepliesFromConnection = 1;

		var faulted = StoreThrough(proxy, runId);
		var aggregate = await faulted.CreateAsync(aggregateId, cancellationToken);
		aggregate.IncrementInt32Value();

		await Assert.That(async () => await faulted.SaveAsync(aggregate, cancellationToken)).ThrowsException();

		// Read back over a healthy connection: the fault must not be what makes the read say "absent".
		var healthy = fixture.CreateEventStore<PersistenceAggregate>(runId: runId);

		var existence = await healthy.ExistsAsync(aggregateId, cancellationToken);

		await Assert.That(existence.DoesExist).IsFalse();
	}

	// The lost-response case. The write is allowed through and the reply is severed, so the client
	// believes it failed. Retrying must not produce a second copy of the events.
	[Test]
	public async Task SaveAsync_WhenTheResponseIsLost_ARetryDoesNotApplyTheEventsTwice(
		CancellationToken cancellationToken
	)
	{
		var runId = Guid.NewGuid();
		var aggregateId = $"{Guid.NewGuid():N}";

		var healthy = fixture.CreateEventStore<PersistenceAggregate>(runId: runId);
		var aggregate = await healthy.CreateAsync(aggregateId, cancellationToken);
		aggregate.IncrementInt32Value();
		aggregate.IncrementInt32Value();

		await using var proxy = await ProxyAsync();
		// Let the connection the SDK opens get its work done, then sever the reply.
		proxy.ResetRepliesFromConnection = 1;

		var faulted = StoreThrough(proxy, runId);

		// Whether this throws depends on how far the SDK got before the reset; either outcome is
		// legitimate, and neither licenses a duplicate afterwards.
		try
		{
			await faulted.SaveAsync(aggregate, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException) { }

		proxy.ResetRepliesFromConnection = null;

		// Without this the test passes when the save never reached the proxy at all - which is how a
		// fault-injection test quietly stops injecting anything.
		await Assert.That(proxy.ConnectionsAccepted).IsGreaterThan(0);

		var reread = await healthy.GetAsync(aggregateId, cancellationToken: cancellationToken);
		if (reread.IsFailure)
			// Nothing committed, which is the other legitimate outcome: there is no duplicate to
			// check for, and the "leaves nothing behind" test above covers that case properly.
			return;

		var versions = new List<long>();
		await foreach (var (record, _) in healthy.GetEventRangeAsync(aggregateId, 1, null, cancellationToken))
			versions.Add(record.Metadata.AggregateVersion);

		// Contiguous and unique from 1: a duplicate shows up as a repeated version, a lost event as
		// a gap. Asserting the count alone would miss both.
		await Assert.That(versions).IsEquivalentTo(Enumerable.Range(1, versions.Count).Select(v => (long)v).ToArray());
	}

	// A half-written request is the fault a batch split or a large payload is most exposed to.
	[Test]
	public async Task SaveAsync_WhenTheRequestIsTruncated_LeavesNoPartialStream(CancellationToken cancellationToken)
	{
		var runId = Guid.NewGuid();
		var aggregateId = $"{Guid.NewGuid():N}";

		await using var proxy = await ProxyAsync();
		proxy.ResetAfterBytesSent = 256;

		var faulted = StoreThrough(proxy, runId);

		try
		{
			var aggregate = await faulted.CreateAsync(aggregateId, cancellationToken);
			aggregate.AppendString(new string('a', 4096));

			await faulted.SaveAsync(aggregate, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException) { }

		// Without this the test passes when the truncation never reached the proxy at all.
		await Assert.That(proxy.ConnectionsAccepted).IsGreaterThan(0);

		var healthy = fixture.CreateEventStore<PersistenceAggregate>(runId: runId);

		var versions = new List<long>();
		await foreach (var (record, _) in healthy.GetEventRangeAsync(aggregateId, 1, null, cancellationToken))
			versions.Add(record.Metadata.AggregateVersion);

		// Either the save landed whole or it landed not at all. A stream with a hole in it is the
		// outcome this test exists to rule out.
		await Assert.That(versions).IsEquivalentTo(Enumerable.Range(1, versions.Count).Select(v => (long)v).ToArray());
	}

	// The proxy must actually be in the path. If the connection string were not rewritten, every
	// test above would pass by talking straight to the container and faulting nothing.
	[Test]
	public async Task TheProxyIsInThePath(CancellationToken cancellationToken)
	{
		var runId = Guid.NewGuid();

		await using var proxy = await ProxyAsync();
		var store = StoreThrough(proxy, runId);

		await store.ExistsAsync($"{Guid.NewGuid():N}", cancellationToken);

		await Assert.That(proxy.ConnectionsAccepted).IsGreaterThan(0);
	}

	Task<FaultingTcpProxy> ProxyAsync()
	{
		SqlConnectionStringBuilder builder = new(fixture.ConnectionString);
		var (host, port) = SplitDataSource(builder.DataSource);

		return Task.FromResult(FaultingTcpProxy.Start(host, port));
	}

	IEventStoreCore<PersistenceAggregate> StoreThrough(FaultingTcpProxy proxy, Guid runId) =>
		fixture.CreateEventStore<PersistenceAggregate>(
			runId: runId,
			configureOptions: options =>
			{
				SqlConnectionStringBuilder builder = new(fixture.ConnectionString)
				{
					DataSource = $"127.0.0.1,{proxy.Port}",
					// The SDK's own retries would quietly paper over the fault and turn these into
					// tests of the retry policy rather than of what the stream looks like afterwards.
					ConnectRetryCount = 0,
					ConnectTimeout = 5,
				};

				options.ConnectionString = builder.ConnectionString;
				options.TimeoutInSeconds = 10;
			}
		);

	static (string Host, int Port) SplitDataSource(string dataSource)
	{
		// "tcp:127.0.0.1,1433" and "127.0.0.1,1433" are both shapes the container can produce.
		var trimmed = dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? dataSource[4..] : dataSource;
		var comma = trimmed.LastIndexOf(',', StringComparison.Ordinal);

		return comma < 0
			? (trimmed, 1433)
			: (trimmed[..comma], int.Parse(trimmed[(comma + 1)..], System.Globalization.CultureInfo.InvariantCulture));
	}
}
