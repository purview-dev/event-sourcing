using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Retention;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Contracts;

/// <summary>
/// Provider-agnostic contract for removing events from a stream.
/// </summary>
/// <remarks>
/// <para>
/// This is the only destructive operation in the framework, so the contract is about what
/// <em>survives</em>: everything above the prune point, the aggregate's version, and every other
/// tenant's data.
/// </para>
/// <para>
/// Pruning is called directly here rather than through <c>IEventStreamRetention</c>, because the
/// policy rules are tested separately against the preview. What a provider has to get right is the
/// delete itself.
/// </para>
/// </remarks>
/// <typeparam name="TAggregate">The aggregate type under test.</typeparam>
public abstract class EventStorePruningTestsBase<TAggregate>
	where TAggregate : class, IAggregateTest, new()
{
	/// <summary>
	/// Creates a store scoped to <paramref name="scope"/>, sharing storage with every other store
	/// built from the same <paramref name="runId"/>.
	/// </summary>
	protected abstract IEventStoreCore<TAggregate> CreateTenantScopedEventStore(TenantScope scope, Guid runId);

	[Test]
	public async Task PruneEventsAsync_RemovesEventsUpToAndIncludingTheVersion(CancellationToken cancellationToken)
	{
		var eventStore = CreateTenantScopedEventStore(TenantScope.System, Guid.NewGuid());
		var aggregateId = await SeedAsync(eventStore, events: 6, cancellationToken);

		var removed = await Pruning(eventStore).PruneEventsAsync(aggregateId, 3, cancellationToken);

		await Assert.That(removed).IsEqualTo(3);
	}

	// The events above the prune point are the ones replay still needs. Removing any of them would
	// be unrecoverable, so this is the assertion that matters most.
	[Test]
	public async Task PruneEventsAsync_LeavesEventsAboveThePrunePoint(CancellationToken cancellationToken)
	{
		var eventStore = CreateTenantScopedEventStore(TenantScope.System, Guid.NewGuid());
		var aggregateId = await SeedAsync(eventStore, events: 6, cancellationToken);

		await Pruning(eventStore).PruneEventsAsync(aggregateId, 3, cancellationToken);

		var remaining = 0;
		await foreach (
			var _ in ((IAggregateEventHistoryStoreCore<TAggregate>)eventStore).GetEventRangeAsync(
				aggregateId,
				1,
				null,
				cancellationToken
			)
		)
			remaining++;

		await Assert.That(remaining).IsEqualTo(3);
	}

	[Test]
	public async Task PruneEventsAsync_RunTwice_HasNothingLeftToRemove(CancellationToken cancellationToken)
	{
		var eventStore = CreateTenantScopedEventStore(TenantScope.System, Guid.NewGuid());
		var aggregateId = await SeedAsync(eventStore, events: 4, cancellationToken);

		await Pruning(eventStore).PruneEventsAsync(aggregateId, 2, cancellationToken);
		var second = await Pruning(eventStore).PruneEventsAsync(aggregateId, 2, cancellationToken);

		await Assert.That(second).IsEqualTo(0);
	}

	// A destructive operation that forgot the tenant would not leak data, it would destroy someone
	// else's.
	[Test]
	public async Task PruneEventsAsync_RunAsOneTenant_LeavesAnotherTenantsStream(CancellationToken cancellationToken)
	{
		var runId = Guid.NewGuid();
		var acme = CreateTenantScopedEventStore(TenantScope.For("acme"), runId);
		var globex = CreateTenantScopedEventStore(TenantScope.For("globex"), runId);

		var acmeId = await SeedAsync(acme, events: 4, cancellationToken);
		var globexId = await SeedAsync(globex, events: 4, cancellationToken);

		await Pruning(acme).PruneEventsAsync(acmeId, 4, cancellationToken);

		// Another tenant's stream, pruned by its own scope, must still have everything to remove.
		var globexRemoved = await Pruning(globex).PruneEventsAsync(globexId, 4, cancellationToken);

		await Assert.That(globexRemoved).IsEqualTo(4);
	}

	[Test]
	public async Task PruneEventsAsync_GivenAnInvalidVersion_Throws(CancellationToken cancellationToken)
	{
		var eventStore = CreateTenantScopedEventStore(TenantScope.System, Guid.NewGuid());
		var aggregateId = await SeedAsync(eventStore, events: 2, cancellationToken);

		await Assert
			.That(async () => await Pruning(eventStore).PruneEventsAsync(aggregateId, 0, cancellationToken))
			.Throws<ArgumentOutOfRangeException>();
	}

	static IEventStreamPruning<TAggregate> Pruning(IEventStoreCore<TAggregate> eventStore) =>
		(IEventStreamPruning<TAggregate>)eventStore;

	static async Task<string> SeedAsync(
		IEventStoreCore<TAggregate> eventStore,
		int events,
		CancellationToken cancellationToken
	)
	{
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = await eventStore.CreateAsync(aggregateId, cancellationToken);
		for (var i = 0; i < events; i++)
			aggregate.AppendString($"event-{i}");

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		return aggregateId;
	}
}
