using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.MongoDB.StorageClient;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.MongoDB.Snapshots;

/// <summary>
/// A MongoDB-backed, queryable event store for <typeparamref name="T"/> that combines an event stream with
/// snapshot documents stored in MongoDB.
/// </summary>
/// <typeparam name="T">An <see cref="IAggregate"/> implementation.</typeparam>
/// <remarks>
/// Persistence of events, deletes and restores is delegated to an underlying non-queryable event store,
/// while snapshot documents are stored in a MongoDB collection and used to serve queryable reads. Snapshots
/// are always reconstructible from the event stream and never become the sole source of aggregate state.
/// </remarks>
/// <seealso cref="IMongoDBSnapshotEventStore{T}"/>
public sealed partial class MongoDBSnapshotEventStore<T> : IMongoDBSnapshotEventStore<T>, IDisposable
	where T : AggregateBase, new()
{
	readonly IEventStoreCore<T> _eventStore;
	readonly MongoDBClient _mongoDBClient;
	readonly IOptions<MongoDBSnapshotEventStoreOptions> _mongoDBOptions;
	readonly IMongoDBSnapshotEventStoreTelemetry _telemetry;
	readonly ISnapshotStrategy<T> _snapshotStrategy;
	readonly ISnapshotStrategySelector? _snapshotStrategySelector;

	readonly string _aggregateName;

	/// <summary>
	/// Initializes a new <see cref="MongoDBSnapshotEventStore{T}"/> instance.
	/// </summary>
	/// <param name="eventStore">The underlying non-queryable event store that persists events, deletes and restores.</param>
	/// <param name="mongoDbOptions">The options controlling MongoDB connection, database and collection configuration.</param>
	/// <param name="telemetry">The telemetry contract used to trace snapshot operations.</param>
	/// <param name="mongoDBClientTelemetry">The telemetry contract used to trace MongoDB client operations.</param>
	/// <param name="snapshotStrategy">Optional strategy controlling when snapshots are taken; defaults to always snapshotting.</param>
	/// <param name="snapshotStrategySelector">Optional selector used to resolve the snapshot strategy for an operation.</param>
	/// <param name="currentTenant">Optional tenant scope applied to snapshot reads and writes.</param>
	public MongoDBSnapshotEventStore(
		Internal.INonQueryableEventStore<T> eventStore,
		IOptions<MongoDBSnapshotEventStoreOptions> mongoDbOptions,
		IMongoDBSnapshotEventStoreTelemetry telemetry,
		IMongoDBClientTelemetry mongoDBClientTelemetry,
		ISnapshotStrategy<T>? snapshotStrategy = null,
		ISnapshotStrategySelector? snapshotStrategySelector = null,
		ICurrentTenant? currentTenant = null
	)
	{
		_eventStore = eventStore;
		_mongoDBOptions = mongoDbOptions;
		_telemetry = telemetry;
		_snapshotStrategy = snapshotStrategy ?? new AlwaysSnapshotStrategy<T>();
		_snapshotStrategySelector = snapshotStrategySelector;

		_aggregateName = TypeNameHelper.GetName(typeof(T), "Aggregate");
		var collectionName = _mongoDBOptions.Value.Collection ?? $"snapshot-{_aggregateName}-store";
		_mongoDBClient = new(
			mongoDBClientTelemetry,
			new()
			{
				ConnectionString = _mongoDBOptions.Value.ConnectionString,
				Database = _mongoDBOptions.Value.Database,
				Collection = collectionName,
				ApplicationName = _mongoDBOptions.Value.ApplicationName,
			},
			currentTenant: currentTenant
		);
	}

	///<inheritdoc/>
	public async Task SnapshotAsync(T aggregate, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		if (await _mongoDBClient.UpsertAsync(aggregate, BuildPredicate(aggregate), cancellationToken))
			_telemetry.SnapshotCreated(_aggregateName);
	}

	static FilterDefinition<T> BuildPredicate(T aggregate)
	{
		var predicate = new FilterDefinitionBuilder<T>().Eq(
			MongoDBAggregateSerializer<T>.BsonDocuemntIdPropertyName,
			aggregate.Id()
		);

		return predicate;
	}

	/// <summary>
	/// Releases the MongoDB client resources held by the store.
	/// </summary>
	public void Dispose()
	{
		GC.SuppressFinalize(this);
		_mongoDBClient?.Dispose();
	}
}
