using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Contracts;
using Purview.EventSourcing.Fixtures.MongoDB;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.MongoDB.Events;

[GenerateGenericTest(typeof(PersistenceAggregate))]
[ClassDataSource<MongoDBEventStoreFixture>(Shared = SharedType.PerTestSession)]
[InheritsTests]
public sealed class EventStoreIdempotencyMarkerRetentionTests<TAggregate>(MongoDBEventStoreFixture fixture)
	: EventStoreIdempotencyMarkerRetentionTestsBase<TAggregate>
	where TAggregate : class, IAggregateTest, new()
{
	protected override IEventStoreCore<TAggregate> CreateTenantScopedEventStore(TenantScope scope, Guid runId) =>
		fixture.CreateEventStore<TAggregate>(runId: runId, currentTenant: new EventStoreTenantScopeStub(scope));
}
