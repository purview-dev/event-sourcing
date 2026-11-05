using Microsoft.Extensions.Caching.Distributed;
using Purview.Containers.PostgreSql;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.Postgres.Events;
using Purview.EventSourcing.Services;
using Purview.EventSourcing.Tenancy;
using TUnit.Core.Interfaces;

namespace Purview.EventSourcing.Fixtures.Postgres;

public class PostgresEventStoreFixture : IAsyncInitializer, IAsyncDisposable
{
	readonly PostgreSqlContainer _postgresContainer = ContainerHelper.CreatePostgreSql();

	IAggregateEventNameMapper _eventNameMapper = default!;

	public PostgresEventStoreFixture()
	{
		EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = false;
	}

	public IDistributedCacheMock Cache { get; private set; } = default!;

	public IPostgresEventStoreTelemetryMock Telemetry { get; private set; } = default!;

	internal PostgresEventStoreClient Client { get; private set; } = default!;

	public string ConnectionString => _postgresContainer.GetConnectionString();

	public PostgresEventStore<TAggregate> CreateEventStore<TAggregate>(
		IAggregateChangeFeedNotifier<TAggregate>? aggregateChangeNotifier = null,
		bool removeFromCacheOnDelete = false,
		Guid? runId = null,
		Action<PostgresEventStoreOptions>? configureOptions = null,
		ICurrentTenant? currentTenant = null
	)
		where TAggregate : class, IAggregate, new() =>
		CreateEventStoreContext(
			aggregateChangeNotifier,
			removeFromCacheOnDelete,
			runId,
			configureOptions,
			currentTenant
		).EventStore;

	internal (
		PostgresEventStore<TAggregate> EventStore,
		PostgresEventStoreClient Client,
		IDistributedCacheMock Cache,
		IPostgresEventStoreTelemetryMock Telemetry
	) CreateEventStoreContext<TAggregate>(
		IAggregateChangeFeedNotifier<TAggregate>? aggregateChangeNotifier = null,
		bool removeFromCacheOnDelete = false,
		Guid? runId = null,
		Action<PostgresEventStoreOptions>? configureOptions = null,
		ICurrentTenant? currentTenant = null
	)
		where TAggregate : class, IAggregate, new()
	{
		runId ??= Guid.NewGuid();
		var cache = CreateDistributedCache();
		Cache = cache;
		var telemetry = IPostgresEventStoreTelemetry.Mock();
		Telemetry = telemetry;
		_eventNameMapper = new AggregateEventNameMapper();

		var aggregateRequirementsManager = IAggregateRequirementsManager.Mock();
		PostgresEventStoreOptions options = new()
		{
			ConnectionString = ConnectionString,
			TableName = $"EventStoreEvents_{runId:N}",
			SchemaName = "public",
			AutoCreateTable = true,
			TimeoutInSeconds = 60,
			RemoveDeletedFromCache = removeFromCacheOnDelete,
		};
		configureOptions?.Invoke(options);

		PostgresEventStoreClient client = new(options, currentTenant);
		Client = client;

		PostgresEventStore<TAggregate> eventStore = new(
			eventNameMapper: _eventNameMapper,
			sqlServerOptions: Microsoft.Extensions.Options.Options.Create(options),
			distributedCache: cache,
			eventStoreTelemetry: telemetry,
			aggregateChangeNotifier: aggregateChangeNotifier ?? IAggregateChangeFeedNotifier<TAggregate>.Mock(),
			aggregateRequirementsManager: aggregateRequirementsManager,
			currentTenant: currentTenant
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
		await _postgresContainer.StartAsync();
	}

	public async ValueTask DisposeAsync()
	{
		await _postgresContainer.DisposeAsync();
		GC.SuppressFinalize(this);
	}
}
