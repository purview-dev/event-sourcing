using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Contracts;
using Purview.EventSourcing.CosmosDb.Snapshot;
using Purview.EventSourcing.Fixtures.CosmosDb;

namespace Purview.EventSourcing.CosmosDb.Snapshots;

[GenerateGenericTest(typeof(PersistenceAggregate))]
[ClassDataSource<CosmosDbSnapshotEventStoreFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(CosmosDbTestConstraints.Container)]
[InheritsTests]
public sealed class SnapshotStoreContractTests<TAggregate>(CosmosDbSnapshotEventStoreFixture fixture)
	: SnapshotStoreContractTestsBase<TAggregate>,
		IAsyncDisposable
	where TAggregate : class, IAggregateTest, new()
{
	CosmosDbSnapshotEventStoreContext? _context;

	protected override IQueryableEventStoreCore<TAggregate> CreateSnapshotStore()
	{
		// The Cosmos snapshot fixture is bound to PersistenceAggregate.
		_context = fixture.CreateContext();
		return (IQueryableEventStoreCore<TAggregate>)(object)_context.EventStore;
	}

	protected override Task SnapshotAsync(
		IQueryableEventStoreCore<TAggregate> store,
		TAggregate aggregate,
		CancellationToken cancellationToken
	) =>
		((CosmosDbSnapshotEventStore<PersistenceAggregate>)(object)store).SnapshotAsync(
			(PersistenceAggregate)(object)aggregate,
			cancellationToken
		);

	public async ValueTask DisposeAsync()
	{
		if (_context != null)
			await _context.DisposeAsync();
	}
}
