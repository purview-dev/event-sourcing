using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.Contracts;
using Purview.EventSourcing.Fixtures.MongoDB;
using Purview.EventSourcing.MongoDB.StorageClient;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.MongoDB.Events;

[GenerateGenericTest(typeof(PersistenceAggregate))]
[ClassDataSource<MongoDBEventStoreFixture>(Shared = SharedType.PerTestSession)]
[InheritsTests]
public sealed class EventStoreContractTests<TAggregate>(MongoDBEventStoreFixture fixture)
	: EventStoreContractTestsBase<TAggregate>
	where TAggregate : class, IAggregateTest, new()
{
	(
		MongoDBEventStore<TAggregate> EventStore,
		IMongoDBEventStoreTelemetryMock Telemetry,
		Microsoft.Extensions.Caching.Distributed.IDistributedCacheMock Cache,
		MongoDBClient EventClient,
		MongoDBClient SnapshotClient
	)? _ctx;

	protected override IEventStoreCore<TAggregate> CreateEventStore()
	{
		var ctx = fixture.CreateEventStoreContext<TAggregate>();
		_ctx = ctx;
		return ctx.EventStore;
	}

	protected override IEventStoreCore<TAggregate> CreateEventStore(
		IAggregateChangeFeedNotifier<TAggregate>? changeFeedNotifier
	)
	{
		var ctx = fixture.CreateEventStoreContext(aggregateChangeNotifier: changeFeedNotifier);
		_ctx = ctx;
		return ctx.EventStore;
	}

	protected override async Task MarkEventTypesAsUnknownAsync(
		string aggregateId,
		string aggregateType,
		int fromVersion,
		int toVersion,
		string eventType,
		CancellationToken cancellationToken
	)
	{
		var ctx = _ctx ??= fixture.CreateEventStoreContext<TAggregate>();
		var eventStore = ctx.EventStore;
		var eventClient = ctx.EventClient;

		// Update existing events to make them unknown types effectively.
		var eventsToUpdate = eventStore.GetEventRangeEntitiesAsync(
			aggregateId,
			fromVersion,
			toVersion,
			cancellationToken
		);

		BatchOperation batchOperation = new();
		var batch = batchOperation;
		await foreach (var eventToUpdate in eventsToUpdate)
		{
			eventToUpdate.EventType = eventType;

			batch.Update(eventToUpdate);
		}

		await eventClient.SubmitBatchAsync(batch, cancellationToken);
	}

	protected override IEventStoreCore<TAggregate> CreateTenantScopedEventStore(TenantScope scope, Guid runId) =>
		fixture.CreateEventStore<TAggregate>(currentTenant: new EventStoreTenantScopeStub(scope), runId: runId);
}
