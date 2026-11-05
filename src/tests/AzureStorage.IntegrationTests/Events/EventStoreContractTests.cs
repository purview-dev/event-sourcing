using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.AzureStorage.StorageClients.Blob;
using Purview.EventSourcing.AzureStorage.StorageClients.Table;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.Contracts;
using Purview.EventSourcing.Fixtures.AzureStorage;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.AzureStorage.Events;

[GenerateGenericTest(typeof(PersistenceAggregate))]
[ClassDataSource<TableEventStoreFixture>(Shared = SharedType.PerTestSession)]
[InheritsTests]
public sealed class EventStoreContractTests<TAggregate>(TableEventStoreFixture fixture)
	: EventStoreContractTestsBase<TAggregate>
	where TAggregate : class, IAggregateTest, new()
{
	(
		TableEventStore<TAggregate> EventStore,
		ITableEventStoreTelemetryMock Telemetry,
		Microsoft.Extensions.Caching.Distributed.IDistributedCacheMock Cache,
		AzureTableClient TableClient,
		AzureBlobClient BlobClient
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
		var tableClient = ctx.TableClient;

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

			batch.Update(eventToUpdate, merge: false);
		}

		await tableClient.SubmitBatchAsync(batch, cancellationToken);
	}

	protected override IEventStoreCore<TAggregate> CreateTenantScopedEventStore(TenantScope scope, Guid runId) =>
		fixture.CreateEventStore<TAggregate>(currentTenant: new EventStoreTenantScopeStub(scope), runId: runId);
}
