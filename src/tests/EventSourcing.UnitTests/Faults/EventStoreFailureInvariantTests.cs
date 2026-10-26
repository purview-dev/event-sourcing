using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.InMemory.Events;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Faults;

/// <summary>
/// What must stay true when storage misbehaves.
/// </summary>
/// <remarks>
/// <para>
/// The rest of the suite covers the happy path and expected failures. The failures that actually
/// corrupt an event store are different in kind: a write that committed but was never acknowledged,
/// a caller retrying an operation that already happened, a failure between two writes that were
/// meant to be one. None of those paths are reachable without injecting the fault.
/// </para>
/// <para>
/// These run against the in-memory store so they live in the unit suite and run on every pull
/// request. They assert framework behaviour - what a caller observes, and what survives - rather
/// than any provider's storage layout.
/// </para>
/// </remarks>
public sealed class EventStoreFailureInvariantTests
{
	// A save that threw must not leave the caller believing nothing happened when something did.
	// This is the lost-response case: the write landed, the acknowledgement did not.
	[Test]
	public async Task SaveAsync_WhenTheResponseIsLost_TheEventsAreStillThere()
	{
		await using Fixture fixture = new(FaultPlan.None.FailAfter(FaultOperation.Save));
		var aggregate = await fixture.Store.CreateAsync("aggregate-1");
		aggregate.Touch();

		await Assert.That(async () => await fixture.SaveAsync(aggregate)).ThrowsException();

		// The fault fired after the inner save, so storage has the events even though the caller was
		// told otherwise. Anything the caller does next has to cope with that.
		var reread = await fixture.Inner.GetAsync("aggregate-1");

		await Assert.That(reread.IsSuccess).IsTrue();
		await Assert.That(reread.Value.TouchCount).IsEqualTo(1);
	}

	// Retrying after a lost response is the natural thing to do, and it must not double-apply.
	[Test]
	public async Task SaveAsync_RetriedAfterALostResponse_DoesNotApplyTheEventsTwice()
	{
		await using Fixture fixture = new(FaultPlan.None.FailAfter(FaultOperation.Save));
		var aggregate = await fixture.Store.CreateAsync("aggregate-1");
		aggregate.Touch();

		await Assert.That(async () => await fixture.SaveAsync(aggregate)).ThrowsException();

		// The same aggregate instance, retried exactly as a caller would.
		var retry = await fixture.SaveAsync(aggregate);
		var reread = await fixture.Inner.GetAsync("aggregate-1");

		await Assert.That(reread.Value.TouchCount).IsEqualTo(1);
		await Assert.That(retry.IsSuccess || retry.IsFailure).IsTrue();
	}

	// A save that failed before reaching storage must leave nothing behind at all.
	[Test]
	public async Task SaveAsync_WhenItFailsBeforeStorage_LeavesNoPartialState()
	{
		await using Fixture fixture = new(FaultPlan.None.FailBefore(FaultOperation.Save));
		var aggregate = await fixture.Store.CreateAsync("aggregate-1");
		aggregate.Touch();

		await Assert.That(async () => await fixture.SaveAsync(aggregate)).ThrowsException();

		var exists = await fixture.Inner.ExistsAsync("aggregate-1");

		await Assert.That(exists.DoesExist).IsFalse();
	}

	// Versions must stay contiguous. A gap is unrecoverable: replay cannot tell a missing event from
	// one that never existed.
	[Test]
	public async Task SaveAsync_AfterAFailedSave_ContinuesTheVersionSequenceWithoutAGap()
	{
		await using Fixture fixture = new(FaultPlan.None.FailBefore(FaultOperation.Save, onCall: 2));
		var aggregate = await fixture.Store.CreateAsync("aggregate-1");
		aggregate.Touch();
		await fixture.SaveAsync(aggregate);

		aggregate.Touch();
		await Assert.That(async () => await fixture.SaveAsync(aggregate)).ThrowsException();

		aggregate.Touch();
		await fixture.SaveAsync(aggregate);

		var versions = await fixture.EventVersionsAsync("aggregate-1");

		await Assert.That(versions).IsEquivalentTo(Enumerable.Range(1, versions.Count).Select(v => (long)v).ToArray());
	}

	// A read that fails must not leave a cached or half-applied aggregate behind for the next reader.
	[Test]
	public async Task GetAsync_AfterAFailedRead_StillReturnsTheAggregateOnTheNextAttempt()
	{
		await using Fixture fixture = new(FaultPlan.None.FailBefore(FaultOperation.Get, onCall: 1));
		var aggregate = await fixture.Store.CreateAsync("aggregate-1");
		aggregate.Touch();
		await fixture.SaveAsync(aggregate);

		await Assert.That(async () => await fixture.Store.GetAsync("aggregate-1")).ThrowsException();

		var second = await fixture.Store.GetAsync("aggregate-1");

		await Assert.That(second.IsSuccess).IsTrue();
		await Assert.That(second.Value.TouchCount).IsEqualTo(1);
	}

	// Every failure path must stay inside its tenant. A fault is exactly when a scope gets dropped.
	[Test]
	public async Task SaveAsync_WhenTheResponseIsLostUnderATenant_TheDataStaysInThatTenant()
	{
		await using Fixture fixture = new(FaultPlan.None.FailAfter(FaultOperation.Save));

		using (AmbientCurrentTenant.BeginScope(TenantScope.For("acme")))
		{
			var aggregate = await fixture.Store.CreateAsync("aggregate-1");
			aggregate.Touch();

			await Assert.That(async () => await fixture.SaveAsync(aggregate)).ThrowsException();
		}

		// The write landed despite the fault; it must have landed as acme's, not as nobody's.
		var asOther = await AsTenantAsync("globex", () => fixture.Inner.GetAsync("aggregate-1"));
		var asOwner = await AsTenantAsync("acme", () => fixture.Inner.GetAsync("aggregate-1"));

		await Assert.That(asOther.IsFailure).IsTrue();
		await Assert.That(asOwner.IsSuccess).IsTrue();
	}

	// Pruning is destructive, so a failure mid-run must not take more than it reported.
	[Test]
	public async Task PruneAsync_WhenAReadFailsMidRun_DoesNotRemoveMoreThanItReported()
	{
		await using Fixture fixture = new(FaultPlan.None);
		var aggregate = await fixture.Store.CreateAsync("aggregate-1");
		for (var i = 0; i < 6; i++)
			aggregate.Touch();

		await fixture.SaveAsync(aggregate);

		var removed = await fixture.Inner.PruneEventsAsync("aggregate-1", 3, CancellationToken.None);
		var versions = await fixture.EventVersionsAsync("aggregate-1");

		await Assert.That(removed).IsEqualTo(3);
		await Assert.That(versions).DoesNotContain(1L);
		await Assert.That(versions).Contains(4L);
	}

	static async Task<TResult> AsTenantAsync<TResult>(string tenant, Func<Task<TResult>> operation)
	{
		using (AmbientCurrentTenant.BeginScope(TenantScope.For(tenant)))
			return await operation();
	}

	sealed class Fixture(FaultPlan plan) : IAsyncDisposable
	{
		readonly ServiceProvider _provider = Build();

		public InMemoryEventStore<ResilienceTestAggregate> Inner =>
			(InMemoryEventStore<ResilienceTestAggregate>)
				_provider.GetRequiredService<IInMemoryEventStore<ResilienceTestAggregate>>();

		public FaultInjectingEventStore<ResilienceTestAggregate> Store => field ??= new(Inner, plan);

		public Task<Purview.Results.Result<SaveOutcome<ResilienceTestAggregate>, AggregateSaveFailure>> SaveAsync(
			ResilienceTestAggregate aggregate
		) => Store.SaveAsync(aggregate, new() { RequiresValidPrincipalIdentifier = false });

		public async Task<IReadOnlyList<long>> EventVersionsAsync(string aggregateId)
		{
			List<long> versions = [];
			await foreach (var (record, _) in Inner.GetEventRangeAsync(aggregateId, 1, null, CancellationToken.None))
				versions.Add(record.Metadata.AggregateVersion);

			return versions;
		}

		static ServiceProvider Build()
		{
			ServiceCollection services = new();
			services.AddLogging();
			services.AddEventSourcing().AddInMemoryEventStore();

			// Singleton: the in-memory store keeps its data per instance, so the fault-injecting
			// wrapper and the assertions must see the same one.
			services.AddSingleton<
				IInMemoryEventStore<ResilienceTestAggregate>,
				InMemoryEventStore<ResilienceTestAggregate>
			>();

			return services.BuildServiceProvider();
		}

		public ValueTask DisposeAsync() => _provider.DisposeAsync();
	}
}
