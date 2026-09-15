using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events.Upcasting;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.MongoDB.Events;
using Purview.EventSourcing.MongoDB.Events.Entities;
using Purview.EventSourcing.MongoDB.StorageClient;
using Purview.EventSourcing.Services;
using Purview.EventSourcing.Validation;

namespace Purview.EventSourcing.MongoDB;

/// <summary>
/// A MongoDB-backed event store for <typeparamref name="T"/> that persists events to MongoDB collections.
/// </summary>
/// <typeparam name="T">An <see cref="IAggregate"/> implementation.</typeparam>
/// <remarks>
/// Events, stream version records and idempotency markers are persisted to an events collection, while
/// snapshots are persisted to a separate snapshot collection. Aggregates are replayed from snapshots and
/// events, and can be cached in an <see cref="IDistributedCache"/>. This store does not support queryable
/// reads; use <see cref="MongoDBSnapshotEventStore{T}"/> for queryable snapshot access.
/// </remarks>
/// <seealso cref="IMongoDBEventStore{T}"/>
/// <seealso cref="MongoDBSnapshotEventStore{T}"/>
[SuppressMessage(
	"Design",
	"CA1506: Avoid excessive class coupling",
	Justification = "MongoDBEventStore is a single logical store split across many partial files; the class-coupling metric is "
		+ "unavoidably inflated for the public surface it must expose."
)]
public sealed partial class MongoDBEventStore<T> : IMongoDBEventStore<T>, IDisposable
	where T : class, IAggregate, new()
{
	readonly MongoDBClient _eventClient;
	readonly MongoDBClient _snapshotClient;

	readonly IAggregateEventNameMapper _eventNameMapper;
	readonly IOptions<MongoDBEventStoreOptions> _eventStoreOptions;
	readonly IAggregateValidator<T>? _validator;
	readonly IAggregateIdFactory? _aggregateIdFactory;
	readonly IDistributedCache _distributedCache;
	readonly IMongoDBEventStoreTelemetry _eventStoreTelemetry;
	readonly ChangeFeed.IAggregateChangeFeedNotifier<T> _aggregateChangeNotifier;
	readonly IAggregateRequirementsManager _aggregateRequirementsManager;
	readonly IEventUpcasterRegistry? _eventUpcasterRegistry;

	readonly string _aggregateTypeFullName;
	readonly string _aggregateTypeShortName;
	readonly int _snapshotSchemaVersion = AggregateSnapshotSchema.GetVersion<T>();

	static readonly ConcurrentDictionary<string, Type> EventTypeCache = new(StringComparer.Ordinal);

	/// <summary>
	/// Initializes a new <see cref="MongoDBEventStore{T}"/> instance.
	/// </summary>
	/// <param name="eventNameMapper">The mapper used to convert between event types and their serialized names.</param>
	/// <param name="mongoDbOptions">The options controlling MongoDB connection, database and collection configuration.</param>
	/// <param name="distributedCache">The cache used to store and retrieve aggregate snapshots.</param>
	/// <param name="eventStoreTelemetry">The telemetry contract used to trace store operations.</param>
	/// <param name="mongoDBClientTelemetry">The telemetry contract used to trace MongoDB client operations.</param>
	/// <param name="aggregateChangeNotifier">The notifier invoked before and after aggregates are saved or deleted.</param>
	/// <param name="aggregateRequirementsManager">The manager used to fulfil aggregate requirements.</param>
	/// <param name="storageNameBuilder">Optional builder used to derive MongoDB database and collection names.</param>
	/// <param name="validator">Optional <see cref="IAggregateValidator{T}"/> used to validate aggregates before they are saved.</param>
	/// <param name="aggregateIdFactory">Optional factory used to generate aggregate ids when none is supplied.</param>
	/// <param name="eventUpcasterRegistry">Optional registry used to upcast legacy events during replay.</param>
	public MongoDBEventStore(
		IAggregateEventNameMapper eventNameMapper,
		[NotNull] IOptions<MongoDBEventStoreOptions> mongoDbOptions,
		IDistributedCache distributedCache,
		IMongoDBEventStoreTelemetry eventStoreTelemetry,
		IMongoDBClientTelemetry mongoDBClientTelemetry,
		ChangeFeed.IAggregateChangeFeedNotifier<T> aggregateChangeNotifier,
		IAggregateRequirementsManager aggregateRequirementsManager,
		IMongoDBEventStoreStorageNameBuilder? storageNameBuilder = null,
		IAggregateValidator<T>? validator = null,
		IAggregateIdFactory? aggregateIdFactory = null,
		IEventUpcasterRegistry? eventUpcasterRegistry = null
	)
	{
		_eventNameMapper = eventNameMapper;
		_eventStoreOptions = mongoDbOptions;
		_validator = validator;
		_aggregateIdFactory = aggregateIdFactory;
		_distributedCache = distributedCache;
		_eventStoreTelemetry = eventStoreTelemetry;
		_aggregateChangeNotifier = aggregateChangeNotifier;
		_aggregateRequirementsManager = aggregateRequirementsManager;
		_eventUpcasterRegistry = eventUpcasterRegistry;

		_aggregateTypeShortName = typeof(T).Name;
		_aggregateTypeFullName = typeof(T).FullName ?? _aggregateTypeShortName;

		var aggregateName = _eventNameMapper.InitializeAggregate<T>();
		if (!aggregateName.Contains('.', StringComparison.InvariantCulture))
			_aggregateTypeShortName = aggregateName;

		_eventClient = new(
			mongoDBClientTelemetry,
			new MongoDBConfiguration
			{
				ApplicationName = mongoDbOptions.Value.ApplicationName,
				Database = storageNameBuilder?.GetDatabaseName<T>() ?? mongoDbOptions.Value.Database,
				Collection =
					storageNameBuilder?.GetEventsCollectionName<T>()
					?? mongoDbOptions.Value.EventCollection
					?? $"es-{_aggregateTypeShortName}-events",
				ConnectionString = mongoDbOptions.Value.ConnectionString,
				ReplicaName = mongoDbOptions.Value.ReplicaName,
			}
		);

		_snapshotClient = new(
			mongoDBClientTelemetry,
			new MongoDBConfiguration
			{
				ApplicationName = mongoDbOptions.Value.ApplicationName,
				Database = storageNameBuilder?.GetDatabaseName<T>() ?? mongoDbOptions.Value.Database,
				Collection =
					storageNameBuilder?.GetSnapshotCollectionName<T>()
					?? mongoDbOptions.Value.SnapshotCollection
					?? $"es-{_aggregateTypeShortName}-snapshots",
				ConnectionString = mongoDbOptions.Value.ConnectionString,
				ReplicaName = mongoDbOptions.Value.ReplicaName,
			}
		);
	}

	///<inheritdoc/>
	public T FulfilRequirements(T aggregate)
	{
		_aggregateRequirementsManager.Fulfil(aggregate);

		return aggregate;
	}

	async Task UpdateCacheAsync(
		T aggregate,
		DistributedCacheEntryOptions? cacheEntryOptions,
		CancellationToken cancellationToken = default
	)
	{
		cacheEntryOptions = GetCacheEntryOptions(cacheEntryOptions);

		try
		{
			var cacheKey = CreateCacheKey(aggregate.Id());
			if (
				aggregate.Details.Locked
				|| (aggregate.Details.IsDeleted && _eventStoreOptions.Value.RemoveDeletedFromCache)
			)
				await _distributedCache.RemoveAsync(cacheKey, cancellationToken);
			else
			{
				if (!_eventStoreOptions.Value.CacheMode.HasFlag(SnapshotCachingOptions.StoreInCache))
					return;

				var data = SerializeSnapshot(aggregate);
				await _distributedCache.SetStringAsync(cacheKey, data, cacheEntryOptions, cancellationToken);
			}
		}
#pragma warning disable CA1031
		catch (Exception ex)
#pragma warning restore CA1031
		{
			_eventStoreTelemetry.CacheUpdateFailure(aggregate.Id(), _aggregateTypeFullName, ex);
		}
	}

	DistributedCacheEntryOptions GetCacheEntryOptions(DistributedCacheEntryOptions? cacheEntryOptions) =>
		cacheEntryOptions ?? new() { SlidingExpiration = _eventStoreOptions.Value.DefaultCacheSlidingDuration };

	///<inheritdoc/>
	public async IAsyncEnumerable<string> GetAggregateIdsAsync(
		bool includeDeleted,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
	{
		Expression<Func<StreamVersionEntity, bool>> whereClause = includeDeleted
			? m => m.AggregateType == _aggregateTypeShortName && m.EntityType == EntityTypes.StreamVersionType
			: m =>
				m.AggregateType == _aggregateTypeShortName
				&& m.EntityType == EntityTypes.StreamVersionType
				&& !m.IsDeleted;

		var query = _eventClient.GetQueryEnumerableAsync(
			whereClause,
			m => m.OrderBy(x => x.AggregateId),
			cancellationToken: cancellationToken
		);
		await foreach (var entity in query)
		{
			if (includeDeleted || !entity.IsDeleted)
				yield return entity.AggregateId.ToString();
		}
	}

	async Task<StreamVersionEntity?> GetStreamVersionAsync(
		string aggregateId,
		bool expectedToExist,
		CancellationToken cancellationToken
	)
	{
		_eventStoreTelemetry.GetStreamVersionStart(aggregateId);

		var elapsedMilliseconds = 0L;
		StreamVersionEntity? result = null;
		try
		{
			var sw = System.Diagnostics.Stopwatch.StartNew();

			result = await _eventClient.GetAsync<StreamVersionEntity>(
				m => m.Id == CreateStreamVersionId(aggregateId) && m.EntityType == EntityTypes.StreamVersionType,
				cancellationToken
			);
			sw.Stop();

			elapsedMilliseconds = sw.ElapsedMilliseconds;

			if (result == null)
			{
				if (expectedToExist)
					_eventStoreTelemetry.StreamVersionExpectedToExistButNotFound(
						aggregateId,
						_aggregateTypeFullName,
						_aggregateTypeShortName
					);
				else
					_eventStoreTelemetry.StreamVersionNotFound(aggregateId);
			}
			else
				_eventStoreTelemetry.StreamVersionFound(
					aggregateId,
					result.Version,
					result.AggregateType,
					result.IsDeleted
				);
		}
#pragma warning disable CA1031
		catch (Exception ex)
#pragma warning restore CA1031
		{
			_eventStoreTelemetry.GetStreamVersionFailed(aggregateId, ex);
		}

		_eventStoreTelemetry.GetStreamVersionComplete(aggregateId, elapsedMilliseconds);

		return result;
	}

	static bool ReturnAggregate(bool isDeleted, string aggregateId, EventStoreOperationContext context)
	{
		if (isDeleted)
		{
#pragma warning disable IDE0010 // Add missing cases
			switch (context.DeleteMode)
			{
				case DeleteHandlingMode.ThrowsException:
					throw AggregateIsDeletedException(aggregateId);
				case DeleteHandlingMode.ReturnsNull:
					return false;
			}
#pragma warning restore IDE0010 // Add missing cases
		}

		return true;
	}

	string CreateStreamVersionId(string aggregateId) => $"s_{_aggregateTypeShortName}_{aggregateId}";

	string CreateEventId(string aggregateId, int version) =>
		$"e_{_aggregateTypeShortName}_{aggregateId}_{$"{version}".PadLeft(_eventStoreOptions.Value.EventSuffixLength, '0')}";

	string CreateIdempotencyCheckId(string aggregateId, string idempotencyId) =>
		$"i_{_aggregateTypeShortName}_{aggregateId}_{idempotencyId}";

#pragma warning disable CA1308 // Normalize strings to uppercase
	/// <summary>
	/// Creates the distributed cache key for the aggregate with the specified <paramref name="aggregateId"/>.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <returns>The case-insensitive cache key used to store and retrieve the aggregate snapshot.</returns>
	public string CreateCacheKey(string aggregateId) =>
		$"{_aggregateTypeShortName}:{aggregateId}{AggregateSnapshotSchema.GetStorageSuffix<T>()}".ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

	/// <summary>
	/// Releases the MongoDB client resources held by the store.
	/// </summary>
	public void Dispose()
	{
		GC.SuppressFinalize(this);

		_eventClient?.Dispose();
		_snapshotClient?.Dispose();
	}
}
