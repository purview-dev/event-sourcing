using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Contracts;
using Purview.EventSourcing.Fixtures.SqlServer;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.SqlServer.Events;

[GenerateGenericTest(typeof(PersistenceAggregate))]
[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerTestSession)]
[InheritsTests]
public sealed class EventStoreIdempotencyMarkerRetentionTests<TAggregate>(SqlServerEventStoreFixture fixture)
	: EventStoreIdempotencyMarkerRetentionTestsBase<TAggregate>
	where TAggregate : class, IAggregateTest, new()
{
	protected override IEventStoreCore<TAggregate> CreateTenantScopedEventStore(TenantScope scope, Guid runId) =>
		fixture.CreateEventStore<TAggregate>(runId: runId, currentTenant: new EventStoreTenantScopeStub(scope));
}
