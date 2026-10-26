using Microsoft.Extensions.Caching.Distributed;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.AzureStorage;
using Purview.EventSourcing.AzureStorage.StorageClients.Blob;
using Purview.EventSourcing.AzureStorage.StorageClients.Table;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.Services;
using Purview.EventSourcing.Tenancy;
using TUnit.Core.Interfaces;

namespace Purview.EventSourcing.Fixtures.AzureStorage;

public sealed class TableEventStoreFixture : IAsyncInitializer, IAsyncDisposable
{
	readonly Containers.Azurite.AzuriteContainer _azuriteContainer = ContainerHelper.CreateAzurite();

	IAggregateEventNameMapper _eventNameMapper = default!;
	IDisposable? _eventStoreAsDisposable;

	public TableEventStoreFixture()
	{
		EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = false;
	}

	/// <summary>The emulator connection string, for tests that build their own storage client.</summary>
	public string ConnectionString => _azuriteContainer.GetEmulatorConnectionString();

	public IDistributedCacheMock Cache { get; private set; } = default!;

	public ITableEventStoreTelemetryMock Telemetry { get; private set; } = default!;

	internal AzureTableClient TableClient { get; private set; } = default!;

	internal AzureBlobClient BlobClient { get; private set; } = default!;

	public TableEventStore<TAggregate> CreateEventStore<TAggregate>(
		IAggregateChangeFeedNotifier<TAggregate>? aggregateChangeNotifier = null,
		bool removeFromCacheOnDelete = false,
		int snapshotRecalculationInterval = 1,
		ICurrentTenant? currentTenant = null,
		Guid? runId = null
	)
		where TAggregate : class, IAggregate, new() =>
		CreateEventStoreContext(
			aggregateChangeNotifier,
			removeFromCacheOnDelete,
			snapshotRecalculationInterval,
			currentTenant,
			runId
		).EventStore;

	internal (
		TableEventStore<TAggregate> EventStore,
		ITableEventStoreTelemetryMock Telemetry,
		IDistributedCacheMock Cache,
		AzureTableClient TableClient,
		AzureBlobClient BlobClient
	) CreateEventStoreContext<TAggregate>(
		IAggregateChangeFeedNotifier<TAggregate>? aggregateChangeNotifier = null,
		bool removeFromCacheOnDelete = false,
		int snapshotRecalculationInterval = 1,
		ICurrentTenant? currentTenant = null,
		Guid? runId = null
	)
		where TAggregate : class, IAggregate, new()
	{
		runId ??= Guid.NewGuid();

		var tableName = TestHelpers.GenAzureTableName(runId);
		var containerName = TestHelpers.GenAzureBlobContainerName(runId);

		var cache = CreateDistributedCache();
		Cache = cache;

		var telemetry = ITableEventStoreTelemetry.Mock();
		Telemetry = telemetry;

		_eventNameMapper = new AggregateEventNameMapper();

		var aggregateRequirementsManager = IAggregateRequirementsManager.Mock();
		AzureStorageEventStoreOptions azureStorageOptions = new()
		{
			ConnectionString = _azuriteContainer.GetEmulatorConnectionString(),
			Table = tableName,
			Container = containerName,
			TimeoutInSeconds = 10,
			RemoveDeletedFromCache = removeFromCacheOnDelete,
		};

		TableEventStore<TAggregate> eventStore = new(
			eventNameMapper: _eventNameMapper,
			azureStorageOptions: Microsoft.Extensions.Options.Options.Create(azureStorageOptions),
			distributedCache: cache,
			aggregateChangeNotifier: aggregateChangeNotifier ?? IAggregateChangeFeedNotifier<TAggregate>.Mock(),
			eventStoreTelemetry: telemetry,
			aggregateRequirementsManager: aggregateRequirementsManager,
			snapshotStrategy: new IntervalSnapshotStrategy<TAggregate>(snapshotRecalculationInterval),
			currentTenant: currentTenant
		);

		AzureTableClient tableClient = new(azureStorageOptions, eventStore.TableName, currentTenant);
		TableClient = tableClient;

		AzureBlobClient blobClient = new(azureStorageOptions, eventStore.ContainerName);
		BlobClient = blobClient;

		_eventStoreAsDisposable = eventStore as IDisposable;

		return (eventStore, telemetry, cache, tableClient, blobClient);
	}

	public static IDistributedCacheMock CreateDistributedCache()
	{
		var cache = IDistributedCache.Mock();
		cache.GetAsync(Any<string>(), Any<CancellationToken>()).Returns((byte[]?)null);

		return cache;
	}

	public async Task InitializeAsync() => await _azuriteContainer.StartAsync();

	public async ValueTask DisposeAsync()
	{
		_eventStoreAsDisposable?.Dispose();

		await _azuriteContainer.DisposeAsync();
	}
}
