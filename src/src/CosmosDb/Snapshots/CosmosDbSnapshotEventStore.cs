using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.CosmosDb.Client;
using Purview.EventSourcing.CosmosDb.Snapshots;
using Purview.EventSourcing.Internal;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.CosmosDb.Snapshot;

/// <summary>
/// An Azure Cosmos DB-backed queryable snapshot event store for <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">An <see cref="IAggregate"/> implementation.</typeparam>
/// <remarks>
/// Aggregates are written as documents into the configured Cosmos DB container, partitioned by
/// <see cref="IAggregate.AggregateType"/>, and mapped by Entity Framework so queries translate against the
/// document itself. Event persistence is delegated to the registered <see cref="INonQueryableEventStore{T}"/>,
/// while snapshots are written according to the configured <see cref="ISnapshotStrategy{T}"/> and telemetry
/// is recorded via <see cref="ICosmosDbSnapshotEventStoreTelemetry"/>.
/// </remarks>
public sealed partial class CosmosDbSnapshotEventStore<T> : ICosmosDbSnapshotEventStore<T>, IAsyncDisposable
	where T : class, IAggregate, new()
{
	readonly IEventStoreCore<T> _eventStore;
	readonly IOptions<CosmosDbEventStoreOptions> _cosmosDbEventStoreOptions;
	readonly ICosmosDbSnapshotEventStoreTelemetry _telemetry;
	readonly ISnapshotStrategy<T> _snapshotStrategy;
	readonly ISnapshotStrategySelector? _snapshotStrategySelector;

	readonly CosmosDbSnapshotClient<T> _cosmosDbClient;

	readonly Type _aggregateType = typeof(T);
	readonly string _aggregateName;

	static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, string> AggregateTypeNames = new();

	/// <summary>
	/// Creates a new <see cref="CosmosDbSnapshotEventStore{T}"/>.
	/// </summary>
	/// <param name="eventStore">The non-queryable event store used to persist the aggregate's events.</param>
	/// <param name="cosmosDbEventStoreOptions">The options used to configure the Cosmos DB connection, container, and indexing.</param>
	/// <param name="telemetry">The telemetry used to record snapshot metrics.</param>
	/// <param name="clientOverrides">Optional, overrides applied to the Cosmos DB client, for endpoints - such as the emulator - the default configuration cannot reach.</param>
	/// <param name="snapshotStrategy">Optional, the strategy used to decide when a snapshot should be written; defaults to <see cref="AlwaysSnapshotStrategy{T}"/>.</param>
	/// <param name="snapshotStrategySelector">Optional, the selector used to resolve a snapshot strategy for the aggregate.</param>
	/// <param name="currentTenant">Optional tenant scope applied to snapshot reads and writes.</param>
	public CosmosDbSnapshotEventStore(
		// Explicitly request a non-queryable event store.
		INonQueryableEventStore<T> eventStore,
		IOptions<CosmosDbEventStoreOptions> cosmosDbEventStoreOptions,
		ICosmosDbSnapshotEventStoreTelemetry telemetry,
		CosmosDbClientOverrides? clientOverrides = null,
		ISnapshotStrategy<T>? snapshotStrategy = null,
		ISnapshotStrategySelector? snapshotStrategySelector = null,
		ICurrentTenant? currentTenant = null
	)
	{
		_eventStore = eventStore;
		_cosmosDbEventStoreOptions = cosmosDbEventStoreOptions;
		_telemetry = telemetry;
		_snapshotStrategy = snapshotStrategy ?? new AlwaysSnapshotStrategy<T>();
		_snapshotStrategySelector = snapshotStrategySelector;

		_cosmosDbClient = new(
			_cosmosDbEventStoreOptions.Value,
			GetAggregateTypeName(),
			clientOverrides,
			currentTenant: currentTenant
		);
		_aggregateName = TypeNameHelper.GetName(_aggregateType, "Aggregate");
	}

	/// <summary>
	/// This will upsert the aggregate regardless of its save state in the internal event store.
	/// </summary>
	/// <param name="aggregate">The aggregate to upsert as a snapshot.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	public async Task SnapshotAsync(T aggregate, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		var result = await _cosmosDbClient.UpsertAsync(aggregate, aggregate.Details.Id, cancellationToken);
		if (result)
			_telemetry.SnapshotCreated(_aggregateName);
	}

	string GetAggregateTypeName() => AggregateTypeNames.GetOrAdd(_aggregateType, _ => new T().AggregateType);

	// Used by the test fixtures to tear down the container they provisioned.
	internal Task DeleteContainerAsync(CancellationToken cancellationToken = default) =>
		_cosmosDbClient.DeleteContainerAsync(cancellationToken);

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await _cosmosDbClient.DisposeAsync();
	}
}
