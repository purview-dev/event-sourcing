using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Fixtures.CosmosDb;

namespace Purview.EventSourcing.CosmosDb.Snapshots;

// Holds the fixture and shared helpers for Cosmos-specific snapshot tests (paging and query
// translation). The provider-agnostic snapshot contract suite lives in SnapshotStoreContractTests.
[ClassDataSource<CosmosDbSnapshotEventStoreFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(CosmosDbTestConstraints.Container)]
public partial class CosmosDbSnapshotEventStoreTests(CosmosDbSnapshotEventStoreFixture fixture)
{
	static PersistenceAggregate CreateAggregate(string? id = null, Action<PersistenceAggregate>? action = null)
	{
		PersistenceAggregate aggregate = new() { Details = { Id = id ?? Guid.NewGuid().ToString() } };

		action?.Invoke(aggregate);

		return aggregate;
	}
}

static class CosmosDbTestConstraints
{
	// Tests share a single emulator container, so they are serialized against this key.
	public const string Container = "CosmosDbContainer";
}
