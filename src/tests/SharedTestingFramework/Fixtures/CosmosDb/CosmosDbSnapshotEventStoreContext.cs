using Microsoft.Extensions.Caching.Distributed;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.AzureStorage;
using Purview.EventSourcing.AzureStorage.StorageClients.Blob;
using Purview.EventSourcing.AzureStorage.StorageClients.Table;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.CosmosDb;
using Purview.EventSourcing.CosmosDb.Snapshot;
using Purview.EventSourcing.CosmosDb.Snapshots;
using Purview.EventSourcing.Services;

namespace Purview.EventSourcing.Fixtures.CosmosDb;

public sealed class CosmosDbSnapshotEventStoreContext(
	string cosmosDbConnectionString,
	CosmosDbClientOverrides cosmosDbClientOverrides,
	string azuriteConnectionString
) : IAsyncDisposable
{
	ITableEventStoreTelemetryMock _telemetry = default!;
	IAggregateEventNameMapper _eventNameMapper = default!;

	public Guid RunId { get; } = Guid.NewGuid();

	public CosmosDbSnapshotEventStore<PersistenceAggregate> EventStore { get; private set; } = default!;

	internal AzureTableClient TableClient { get; private set; } = default!;

	internal AzureBlobClient BlobClient { get; private set; } = default!;

	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Reliability",
		"CA2000:Dispose objects before losing scope",
		Justification = "Used elsewhere."
	)]
	public void CreateCosmosDbEventStore(int correlationIdsToGenerate = 1, string? container = null)
	{
		EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = false;

		var tableEventStore = CreateTableEventStore(correlationIdsToGenerate);

		CosmosDbEventStoreOptions config = new()
		{
			Container = container ?? TestHelpers.GenAzureCosmosDbContainerName(),
			ConnectionString = cosmosDbConnectionString,
			Database = GetType().Name,
			RequestTimeoutInSeconds = 30,
			IgnoreSSLWarnings = true,
			ConnectionMode = Microsoft.Azure.Cosmos.ConnectionMode.Gateway,
		};

		CosmosDbSnapshotEventStore<PersistenceAggregate> eventStore = new(
			tableEventStore,
			Microsoft.Extensions.Options.Options.Create(config),
			ICosmosDbSnapshotEventStoreTelemetry.Mock(),
			cosmosDbClientOverrides
		);

		EventStore = eventStore;
	}

	TableEventStore<PersistenceAggregate> CreateTableEventStore(int correlationIdsToGenerate = 1)
	{
		var runIds = Enumerable
			.Range(1, correlationIdsToGenerate)
			.Select(_ => $"{Guid.NewGuid()}".ToUpperInvariant())
			.ToArray();

		_eventNameMapper = new AggregateEventNameMapper();
		_telemetry = ITableEventStoreTelemetry.Mock();

		AzureStorageEventStoreOptions azureStorageOptions = new()
		{
			ConnectionString = azuriteConnectionString,
			Table = TestHelpers.GenAzureTableName(RunId),
			Container = TestHelpers.GenAzureBlobContainerName(RunId),
			TimeoutInSeconds = 10,
			RemoveDeletedFromCache = true,
		};

		TableEventStore<PersistenceAggregate> eventStore = new(
			eventNameMapper: _eventNameMapper,
			azureStorageOptions: Microsoft.Extensions.Options.Options.Create(azureStorageOptions),
			distributedCache: IDistributedCache.Mock(),
			aggregateChangeNotifier: IAggregateChangeFeedNotifier<PersistenceAggregate>.Mock(),
			eventStoreTelemetry: _telemetry,
			aggregateRequirementsManager: IAggregateRequirementsManager.Mock()
		);

		TableClient = new(azureStorageOptions, eventStore.TableName);
		BlobClient = new(azureStorageOptions, eventStore.ContainerName);

		return eventStore;
	}

	public async ValueTask DisposeAsync()
	{
		await EventStore.DeleteContainerAsync();
		await EventStore.DisposeAsync();
	}
}
