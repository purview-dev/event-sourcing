using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.Contracts;
using Purview.EventSourcing.Fixtures.Postgres;

namespace Purview.EventSourcing.Postgres.Events;

[GenerateGenericTest(typeof(PersistenceAggregate))]
[ClassDataSource<PostgresEventStoreFixture>(Shared = SharedType.PerTestSession)]
[InheritsTests]
public sealed class EventStoreContractTests<TAggregate>(PostgresEventStoreFixture fixture)
	: EventStoreContractTestsBase<TAggregate>
	where TAggregate : class, IAggregateTest, new()
{
	(
		PostgresEventStore<TAggregate> EventStore,
		PostgresEventStoreClient Client,
		Microsoft.Extensions.Caching.Distributed.IDistributedCacheMock Cache,
		IPostgresEventStoreTelemetryMock Telemetry
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
		var client = (_ctx ??= fixture.CreateEventStoreContext<TAggregate>()).Client;

		await foreach (
			var row in client.GetEventRangeAsync(aggregateId, aggregateType, fromVersion, toVersion, cancellationToken)
		)
		{
			await client.UpsertAsync(
				row.Id,
				row.EntityType,
				row.AggregateId,
				row.AggregateType,
				row.Version,
				row.IsDeleted,
				row.Payload,
				eventType,
				row.IdempotencyId,
				row.Timestamp,
				cancellationToken
			);
		}
	}
}
