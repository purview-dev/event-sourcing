using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Fixtures.Postgres;
using Purview.EventSourcing.Outbox;
using Purview.EventSourcing.Postgres.Events;

namespace Purview.EventSourcing.Postgres.Outbox;

// The outbox table name in these tests is a runtime-generated identifier, not user input.
#pragma warning disable CA2100

[ClassDataSource<PostgresEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class PostgresOutboxIntegrationTests(PostgresEventStoreFixture fixture)
{
	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506:Avoid excessive class coupling",
		Justification = "Test wiring: the store, its infrastructure and the result model are all named here."
	)]
	public async Task CommitAsync_GivenEnlistedOutboxAndAggregate_CommitsBothAtomically(
		CancellationToken cancellationToken
	)
	{
		var eventStore = fixture.CreateEventStore<PersistenceAggregate>();
		var aggregateId = $"{Guid.NewGuid():N}";
		var tableName = $"outbox_{Guid.NewGuid():N}";
		var store = CreateStore(tableName);
		var envelope = Envelope(aggregateId, $"outbox-{Guid.NewGuid():N}");

		var aggregate = await eventStore.CreateAsync(aggregateId, cancellationToken);
		aggregate.AppendString("outbox");

		PostgresEventStoreTransactionFactory factory = new(new FixedCorrelationIdProvider("outbox-commit"));
		await using var transaction = factory.CreatePostgresTransaction();
		transaction.Enlist(aggregate, eventStore);
		transaction.Enlist(
			async (connection, sqlTransaction, token) =>
				await store.EnqueueInTransactionAsync(connection, sqlTransaction, envelope, token)
		);

		var result = await transaction.CommitAsync(cancellationToken);
		var savedAggregateResult = await eventStore.GetAsync(aggregateId, null, cancellationToken);
		var savedAggregate = savedAggregateResult.IsSuccess ? savedAggregateResult.Value : null;
		var row = await ReadOutboxRowAsync(tableName, envelope.Id, cancellationToken);

		await Assert.That(result.Success).IsTrue();
		await Assert.That(savedAggregate).IsNotNull();
		await Assert.That(savedAggregate!.StringProperty).Contains("outbox");
		await Assert.That(row).IsNotNull();
		await Assert.That(row!.State).IsEqualTo(OutboxState.Pending);
	}

	[Test]
	public async Task CommitAsync_GivenRollback_DoesNotPersistOutboxOrEvents(CancellationToken cancellationToken)
	{
		var eventStore = fixture.CreateEventStore<PersistenceAggregate>();
		var aggregateId = $"{Guid.NewGuid():N}";
		var tableName = $"outbox_{Guid.NewGuid():N}";
		var store = CreateStore(tableName);
		var envelope = Envelope(aggregateId, $"outbox-{Guid.NewGuid():N}");

		var aggregate = await eventStore.CreateAsync(aggregateId, cancellationToken);
		aggregate.AppendString("outbox");

		PostgresEventStoreTransactionFactory factory = new(new FixedCorrelationIdProvider("outbox-rollback"));
		await using var transaction = factory.CreatePostgresTransaction();
		transaction.Enlist(aggregate, eventStore);
		transaction.Enlist(
			async (connection, sqlTransaction, token) =>
				await store.EnqueueInTransactionAsync(connection, sqlTransaction, envelope, token)
		);
		transaction.Enlist((_, _, _) => Task.FromException(new InvalidOperationException("enlisted operation failed")));

		var result = await transaction.CommitAsync(cancellationToken);
		var savedAggregateResult = await eventStore.GetAsync(aggregateId, null, cancellationToken);
		var savedAggregate = savedAggregateResult.IsSuccess ? savedAggregateResult.Value : null;
		var row = await ReadOutboxRowAsync(tableName, envelope.Id, cancellationToken);

		await Assert.That(result.Success).IsFalse();
		await Assert.That(savedAggregate).IsNull();
		await Assert.That(row).IsNull();
	}

	[Test]
	public async Task DispatchAsync_GivenPendingMessage_InvokesHandlerAndCompletes(CancellationToken cancellationToken)
	{
		var tableName = $"outbox_{Guid.NewGuid():N}";
		var store = CreateStore(tableName);
		RecordingHandler handler = new();
		var dispatcher = CreateDispatcher(store, handler);
		var envelope = Envelope($"agg-{Guid.NewGuid():N}", "dispatch-1");

		await store.EnqueueAsync(envelope, cancellationToken);

		var result = await dispatcher.DispatchAsync(cancellationToken);
		var row = await ReadOutboxRowAsync(tableName, envelope.Id, cancellationToken);

		await Assert.That(result.Claimed).IsEqualTo(1);
		await Assert.That(result.Dispatched).IsEqualTo(1);
		await Assert.That(handler.HandledIds).Contains(envelope.Id);
		await Assert.That(row!.State).IsEqualTo(OutboxState.Dispatched);
	}

	[Test]
	public async Task DispatchAsync_GivenFailingHandler_RetriesThenPoisons(CancellationToken cancellationToken)
	{
		var tableName = $"outbox_{Guid.NewGuid():N}";
		var store = CreateStore(tableName);
		var dispatcher = CreateDispatcher(
			store,
			new FailingHandler(),
			configure: static options =>
			{
				options.MaxAttempts = 2;
				options.RetryBackoffBase = TimeSpan.Zero;
			}
		);
		var envelope = Envelope($"agg-{Guid.NewGuid():N}", "poison-1");

		await store.EnqueueAsync(envelope, cancellationToken);

		var first = await dispatcher.DispatchAsync(cancellationToken);
		var afterFirst = await ReadOutboxRowAsync(tableName, envelope.Id, cancellationToken);

		await Assert.That(first.Failed).IsEqualTo(1);
		await Assert.That(afterFirst!.State).IsEqualTo(OutboxState.Failed);

		var second = await dispatcher.DispatchAsync(cancellationToken);
		var afterSecond = await ReadOutboxRowAsync(tableName, envelope.Id, cancellationToken);

		await Assert.That(second.Poisoned).IsEqualTo(1);
		await Assert.That(afterSecond!.State).IsEqualTo(OutboxState.Poisoned);
		await Assert.That(afterSecond.LastError).IsNotEmpty();
	}

	[Test]
	public async Task EnqueueAsync_GivenDuplicateIdempotencyKey_Deduplicates(CancellationToken cancellationToken)
	{
		var tableName = $"outbox_{Guid.NewGuid():N}";
		var store = CreateStore(tableName);
		var first = Envelope("agg-1", "dedup-1") with { IdempotencyKey = "shared-key" };
		var second = Envelope("agg-1", "dedup-2") with { IdempotencyKey = "shared-key" };

		var insertedFirst = await store.EnqueueAsync(first, cancellationToken);
		var insertedSecond = await store.EnqueueAsync(second, cancellationToken);
		var rowCount = await CountOutboxRowsAsync(tableName, "shared-key", cancellationToken);

		await Assert.That(insertedFirst).IsEqualTo(1);
		await Assert.That(insertedSecond).IsEqualTo(0);
		await Assert.That(rowCount).IsEqualTo(1);
	}

	[Test]
	public async Task ConcurrentDispatchers_ClaimDisjointMessages(CancellationToken cancellationToken)
	{
		var tableName = $"outbox_{Guid.NewGuid():N}";
		var store = CreateStore(tableName);
		SlowRecordingHandler handler = new();
		var dispatcher1 = CreateDispatcher(store, handler, leaseDuration: TimeSpan.FromSeconds(30));
		var dispatcher2 = CreateDispatcher(store, handler, leaseDuration: TimeSpan.FromSeconds(30));

		const int messageCount = 20;
		for (var i = 0; i < messageCount; i++)
			await store.EnqueueAsync(Envelope($"agg-{Guid.NewGuid():N}", $"concurrent-{i}"), cancellationToken);

		var firstTask = dispatcher1.DispatchAsync(cancellationToken);
		var secondTask = dispatcher2.DispatchAsync(cancellationToken);
		await Task.WhenAll(firstTask, secondTask);

		await Assert.That(handler.HandledIds.Count).IsEqualTo(messageCount);
		await Assert.That(handler.HandledIds.Distinct().Count()).IsEqualTo(messageCount);
	}

	PostgresOutboxStore CreateStore(string tableName) =>
		new(
			Options.Create(
				new PostgresOutboxStoreOptions { ConnectionString = fixture.ConnectionString, TableName = tableName }
			),
			Options.Create(new PostgresEventStoreOptions { ConnectionString = fixture.ConnectionString }),
			NullLogger<PostgresOutboxStore>.Instance
		);

	static OutboxDispatcher CreateDispatcher(
		PostgresOutboxStore store,
		IOutboxHandler handler,
		Action<OutboxDispatchOptions>? configure = null,
		TimeSpan? leaseDuration = null
	)
	{
		OutboxDispatchOptions options = new();
		configure?.Invoke(options);
		if (leaseDuration is not null)
			options.LeaseDuration = leaseDuration.Value;

		return new OutboxDispatcher(store, handler, Options.Create(options), NullLogger<OutboxDispatcher>.Instance);
	}

	static OutboxEnvelope Envelope(string aggregateId, string id) =>
		new(
			id,
			"PersistenceAggregate",
			aggregateId,
			"StringValueSet",
			/*lang=json,strict*/
			"""{"value":"outbox"}""",
			IdempotencyKey: null,
			CorrelationId: null,
			CreatedUtc: DateTimeOffset.UtcNow
		);

	async Task<OutboxRow?> ReadOutboxRowAsync(string tableName, string id, CancellationToken cancellationToken)
	{
		await using NpgsqlConnection connection = new(fixture.ConnectionString);
		await connection.OpenAsync(cancellationToken);
		await using NpgsqlCommand command = new(
			$"SELECT \"State\", \"AttemptCount\", \"LastError\" FROM public.\"{tableName}\" WHERE \"Id\" = @id",
			connection
		);
		command.Parameters.AddWithValue("id", id);
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		if (!await reader.ReadAsync(cancellationToken))
			return null;

		// The outbox table schema is known and controlled, so we can safely read the columns by index.
		return new(
			(OutboxState)reader.GetInt32(0),
			reader.GetInt32(1),
			await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2)
		);
	}

	async Task<int> CountOutboxRowsAsync(string tableName, string idempotencyKey, CancellationToken cancellationToken)
	{
		await using NpgsqlConnection connection = new(fixture.ConnectionString);
		await connection.OpenAsync(cancellationToken);
		await using NpgsqlCommand command = new(
			$"SELECT COUNT(1) FROM public.\"{tableName}\" WHERE \"IdempotencyKey\" = @key",
			connection
		);
		command.Parameters.AddWithValue("key", idempotencyKey);
		return Convert.ToInt32(
			await command.ExecuteScalarAsync(cancellationToken),
			System.Globalization.CultureInfo.InvariantCulture
		);
	}

	sealed record OutboxRow(OutboxState State, int AttemptCount, string? LastError);

	sealed class RecordingHandler : IOutboxHandler
	{
		public List<string> HandledIds { get; } = [];

		public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
		{
			HandledIds.Add(message.Id);
			return Task.CompletedTask;
		}
	}

	sealed class SlowRecordingHandler : IOutboxHandler
	{
		public ConcurrentBag<string> HandledIds { get; } = [];

		public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
		{
			await Task.Delay(10, cancellationToken);
			HandledIds.Add(message.Id);
		}
	}

	sealed class FailingHandler : IOutboxHandler
	{
		public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken) =>
			throw new InvalidOperationException("handler failed");
	}

	sealed class FixedCorrelationIdProvider(string correlationId) : IEventStoreCorrelationIdProvider
	{
		public string GetCorrelationId() => correlationId;
	}
}
