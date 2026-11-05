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

	/// <summary>
	/// Builds a store wired to the <em>generated</em> telemetry rather than the mock, for tests that
	/// assert spans are actually emitted.
	/// </summary>
	/// <remarks>
	/// A separate method rather than another optional parameter on <c>CreateEventStore</c>: that
	/// signature is called from most of the suite, and widening it counts against the class-coupling
	/// analyzer in every one of those callers.
	/// </remarks>
	public SqlServerEventStore<TAggregate> CreateEventStoreWithRealTelemetry<TAggregate>(
		ISqlServerEventStoreTelemetry telemetry,
		Guid? runId = null
	)
		where TAggregate : class, IAggregate, new() =>
		CreateEventStoreContext<TAggregate>(runId: runId, realTelemetry: telemetry).EventStore;

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
		IEventPayloadProtector? payloadProtector = null,
		ISqlServerEventStoreTelemetry? realTelemetry = null
	)
		where TAggregate : class, IAggregate, new()
	{
		runId ??= Guid.NewGuid();
		var cache = CreateDistributedCache();
		Cache = cache;
		// The mock is the default because almost every test asserts that the store *calls* telemetry.
		// A test that asserts spans are actually *emitted* needs the generated implementation instead:
		// a mock records the call and produces no Activity, so an ActivityListener sees nothing and a
		// span-name assertion written against it passes while proving nothing.
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
			eventStoreTelemetry: realTelemetry ?? telemetry,
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
