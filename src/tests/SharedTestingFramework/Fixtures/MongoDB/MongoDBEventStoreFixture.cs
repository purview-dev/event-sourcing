using Microsoft.Extensions.Caching.Distributed;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.MongoDB;
using Purview.EventSourcing.MongoDB.Events;
using Purview.EventSourcing.MongoDB.StorageClient;
using Purview.EventSourcing.Services;
using Purview.EventSourcing.Tenancy;
using TUnit.Core.Interfaces;

namespace Purview.EventSourcing.Fixtures.MongoDB;

public sealed class MongoDBEventStoreFixture : IAsyncInitializer, IAsyncDisposable
{
	readonly MongoDBTestContainer _mongoDBContainer;

	IAggregateEventNameMapper _eventNameMapper = default!;

	public MongoDBEventStoreFixture()
	{
		EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = false;
		_mongoDBContainer = ContainerHelper.CreateMongoDB();
	}

	public IDistributedCacheMock Cache { get; private set; } = default!;

	public IMongoDBEventStoreTelemetryMock Telemetry { get; private set; } = default!;

	internal MongoDBClient EventClient { get; private set; } = default!;

	internal MongoDBClient SnapshotClient { get; private set; } = default!;

	public MongoDBEventStore<TAggregate> CreateEventStore<TAggregate>(
		IAggregateChangeFeedNotifier<TAggregate>? aggregateChangeNotifier = null,
		bool removeFromCacheOnDelete = false,
		ICurrentTenant? currentTenant = null,
		Guid? runId = null
	)
		where TAggregate : class, IAggregate, new() =>
		CreateEventStoreContext(aggregateChangeNotifier, removeFromCacheOnDelete, currentTenant, runId).EventStore;

	internal (
		MongoDBEventStore<TAggregate> EventStore,
		IMongoDBEventStoreTelemetryMock Telemetry,
		IDistributedCacheMock Cache,
		MongoDBClient EventClient,
		MongoDBClient SnapshotClient
	) CreateEventStoreContext<TAggregate>(
		IAggregateChangeFeedNotifier<TAggregate>? aggregateChangeNotifier = null,
		bool removeFromCacheOnDelete = false,
		ICurrentTenant? currentTenant = null,
		Guid? runId = null
	)
		where TAggregate : class, IAggregate, new()
	{
		runId ??= Guid.NewGuid();

		var cache = CreateDistributedCache();
		Cache = cache;

		var telemetry = IMongoDBEventStoreTelemetry.Mock();
		Telemetry = telemetry;

		_eventNameMapper = new AggregateEventNameMapper();

		var connectionString = _mongoDBContainer.GetConnectionString();

		var aggregateRequirementsManager = IAggregateRequirementsManager.Mock();
		MongoDBEventStoreOptions mongoDBOptions = new()
		{
			ApplicationName = nameof(MongoDBEventStoreFixture),
			ConnectionString = connectionString,
			Database = $"TestDatabase_{runId}",
			EventCollection = $"TestCollection_Events_{runId}",
			SnapshotCollection = $"TestCollection_Snapshots_{runId}",
			ReplicaName = "rs0",
			TimeoutInSeconds = 60,
			RemoveDeletedFromCache = removeFromCacheOnDelete,
		};

		var mongoDBClientTelemetry = IMongoDBClientTelemetry.Mock();
		MongoDBEventStore<TAggregate> eventStore = new(
			eventNameMapper: _eventNameMapper,
			mongoDbOptions: Microsoft.Extensions.Options.Options.Create(mongoDBOptions),
			distributedCache: cache,
			aggregateChangeNotifier: aggregateChangeNotifier ?? IAggregateChangeFeedNotifier<TAggregate>.Mock(),
			eventStoreTelemetry: telemetry,
			mongoDBClientTelemetry: mongoDBClientTelemetry,
			aggregateRequirementsManager: aggregateRequirementsManager,
			currentTenant: currentTenant
		);

		MongoDBClient eventClient = new(
			mongoDBClientTelemetry,
			new() { ConnectionString = mongoDBOptions.ConnectionString, ReplicaName = mongoDBOptions.ReplicaName },
			mongoDBOptions.Database,
			mongoDBOptions.EventCollection
		);
		EventClient = eventClient;

		MongoDBClient snapshotClient = new(
			mongoDBClientTelemetry,
			new() { ConnectionString = mongoDBOptions.ConnectionString, ReplicaName = mongoDBOptions.ReplicaName },
			mongoDBOptions.Database,
			mongoDBOptions.SnapshotCollection
		);
		SnapshotClient = snapshotClient;

		return (eventStore, telemetry, cache, eventClient, snapshotClient);
	}

	public static IDistributedCacheMock CreateDistributedCache()
	{
		var cache = IDistributedCache.Mock();
		cache.GetAsync(Any<string>(), Any<CancellationToken>()).Returns((byte[]?)null);

		return cache;
	}

	public async Task InitializeAsync() => await _mongoDBContainer.StartAsync();

	public async ValueTask DisposeAsync()
	{
		EventClient?.Dispose();
		SnapshotClient?.Dispose();

		await _mongoDBContainer.DisposeAsync();
	}
}
