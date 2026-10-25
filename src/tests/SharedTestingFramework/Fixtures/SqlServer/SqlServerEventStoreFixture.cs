using Microsoft.Extensions.Caching.Distributed;
using Purview.Containers.MsSql;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.Services;
using Purview.EventSourcing.SqlServer.Events;
using Purview.EventSourcing.Tenancy;
using TUnit.Core.Interfaces;

namespace Purview.EventSourcing.Fixtures.SqlServer;

public class SqlServerEventStoreFixture : IAsyncInitializer, IAsyncDisposable
{
	readonly MsSqlContainer _msSqlContainer = ContainerHelper.CreateMsSql();

	IAggregateEventNameMapper _eventNameMapper = default!;

	public SqlServerEventStoreFixture()
	{
		EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = false;
	}

	public IDistributedCacheMock Cache { get; private set; } = default!;

	public ISqlServerEventStoreTelemetryMock Telemetry { get; private set; } = default!;

	internal SqlServerEventStoreClient Client { get; private set; } = default!;

	public string ConnectionString => _msSqlContainer.GetConnectionString();

	public SqlServerEventStore<TAggregate> CreateEventStore<TAggregate>(
		IAggregateChangeFeedNotifier<TAggregate>? aggregateChangeNotifier = null,
		bool removeFromCacheOnDelete = false,
		Guid? runId = null,
		Action<SqlServerEventStoreOptions>? configureOptions = null,
		ICurrentTenant? currentTenant = null,
		IEventPayloadProtector? payloadProtector = null
	)
		where TAggregate : class, IAggregate, new() =>
		CreateEventStoreContext(
			aggregateChangeNotifier,
			removeFromCacheOnDelete,
			runId,
			configureOptions,
			currentTenant,
			payloadProtector
		).EventStore;

	internal (
		SqlServerEventStore<TAggregate> EventStore,
		SqlServerEventStoreClient Client,
		IDistributedCacheMock Cache,
		ISqlServerEventStoreTelemetryMock Telemetry
	) CreateEventStoreContext<TAggregate>(
		IAggregateChangeFeedNotifier<TAggregate>? aggregateChangeNotifier = null,
		bool removeFromCacheOnDelete = false,
		Guid? runId = null,
		Action<SqlServerEventStoreOptions>? configureOptions = null,
		ICurrentTenant? currentTenant = null,
		IEventPayloadProtector? payloadProtector = null
	)
		where TAggregate : class, IAggregate, new()
	{
		runId ??= Guid.NewGuid();
		var cache = CreateDistributedCache();
		Cache = cache;
		var telemetry = ISqlServerEventStoreTelemetry.Mock();
		Telemetry = telemetry;
		_eventNameMapper = new AggregateEventNameMapper();

		var connectionString = _msSqlContainer.GetConnectionString();
		var aggregateRequirementsManager = IAggregateRequirementsManager.Mock();

		SqlServerEventStoreOptions options = new()
		{
			ConnectionString = connectionString,
			TableName = $"EventStoreEvents_{runId:N}",
			SchemaName = "dbo",
			AutoCreateTable = true,
			TimeoutInSeconds = 60,
			RemoveDeletedFromCache = removeFromCacheOnDelete,
		};
		configureOptions?.Invoke(options);

		SqlServerEventStoreClient client = new(options, currentTenant);
		Client = client;

		SqlServerEventStore<TAggregate> eventStore = new(
			eventNameMapper: _eventNameMapper,
			sqlServerOptions: Microsoft.Extensions.Options.Options.Create(options),
			distributedCache: cache,
			eventStoreTelemetry: telemetry,
			aggregateChangeNotifier: aggregateChangeNotifier ?? IAggregateChangeFeedNotifier<TAggregate>.Mock(),
			aggregateRequirementsManager: aggregateRequirementsManager,
			currentTenant: currentTenant,
			payloadProtector: payloadProtector
		);

		return (eventStore, client, cache, telemetry);
	}

	public static IDistributedCacheMock CreateDistributedCache()
	{
		var cache = IDistributedCache.Mock();
		cache.GetAsync(Any<string>(), Any<CancellationToken>()).Returns((byte[]?)null);
		return cache;
	}

	public async Task InitializeAsync()
	{
		await _msSqlContainer.StartAsync();
	}

	public async ValueTask DisposeAsync()
	{
		await _msSqlContainer.DisposeAsync();
		GC.SuppressFinalize(this);
	}
}
