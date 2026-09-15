using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Npgsql;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events.Upcasting;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.Internal;
using Purview.EventSourcing.Services;
using Purview.EventSourcing.Validation;

namespace Purview.EventSourcing.Postgres.Events;

// SQL strings are built from validated identifiers at construction time, not from user input.
#pragma warning disable CA2100

/// <summary>
/// A PostgreSQL-backed event store for <typeparamref name="T"/> aggregates.
/// </summary>
/// <typeparam name="T">An <see cref="IAggregate"/> implementation.</typeparam>
/// <remarks>
/// Events, stream versions, idempotency markers, and snapshots are stored in a single table.
/// <see cref="IPostgresEventStore{T}"/> is registered for the store when
/// <c>AddPostgresEventStore</c> is used.
/// </remarks>
[SuppressMessage(
	"Design",
	"CA1506: Avoid excessive class coupling",
	Justification = "PostgresEventStore is a single logical store split across many partial files; the class-coupling metric is "
		+ "unavoidably inflated for the public surface it must expose."
)]
public sealed partial class PostgresEventStore<T> : IPostgresEventStore<T>, ITransactionalEventStore<T>
	where T : class, IAggregate, new()
{
	const int StreamVersionType = 0;
	const int EventType = 1;
	const int IdempotencyMarkerType = 2;
	const int SnapshotType = 3;

	readonly PostgresEventStoreClient _client;

	readonly IAggregateEventNameMapper _eventNameMapper;
	readonly IOptions<PostgresEventStoreOptions> _eventStoreOptions;
	readonly IAggregateValidator<T>? _validator;
	readonly IAggregateIdFactory? _aggregateIdFactory;
	readonly IDistributedCache _distributedCache;
	readonly IPostgresEventStoreTelemetry _eventStoreTelemetry;
	readonly ChangeFeed.IAggregateChangeFeedNotifier<T> _aggregateChangeNotifier;
	readonly IAggregateRequirementsManager _aggregateRequirementsManager;
	readonly IEventUpcasterRegistry? _eventUpcasterRegistry;

	readonly string _aggregateTypeFullName;
	readonly string _aggregateTypeShortName;
	readonly string _eventIdPrefix;
	readonly string _idempotencyCheckIdPrefix;
	readonly string _snapshotIdPrefix;
	readonly int _snapshotSchemaVersion = AggregateSnapshotSchema.GetVersion<T>();

	static readonly ConcurrentDictionary<string, Type> EventTypeCache = new(StringComparer.Ordinal);

	/// <summary>
	/// Creates a new <see cref="PostgresEventStore{T}"/>.
	/// </summary>
	/// <param name="eventNameMapper">Maps aggregate and event types to their persisted names.</param>
	/// <param name="sqlServerOptions">The event-store options.</param>
	/// <param name="distributedCache">The distributed cache used to cache aggregate snapshots.</param>
	/// <param name="eventStoreTelemetry">The telemetry sink for event-store operations.</param>
	/// <param name="aggregateChangeNotifier">Notifies listeners of aggregate changes.</param>
	/// <param name="aggregateRequirementsManager">Fulfils aggregate requirements after materialization.</param>
	/// <param name="validator">Optional validator used before saving aggregates.</param>
	/// <param name="aggregateIdFactory">Optional factory used to generate aggregate ids.</param>
	/// <param name="eventUpcasterRegistry">Optional registry used to upcast historical events.</param>
	public PostgresEventStore(
		IAggregateEventNameMapper eventNameMapper,
		[NotNull] IOptions<PostgresEventStoreOptions> sqlServerOptions,
		IDistributedCache distributedCache,
		IPostgresEventStoreTelemetry eventStoreTelemetry,
		ChangeFeed.IAggregateChangeFeedNotifier<T> aggregateChangeNotifier,
		IAggregateRequirementsManager aggregateRequirementsManager,
		IAggregateValidator<T>? validator = null,
		IAggregateIdFactory? aggregateIdFactory = null,
		IEventUpcasterRegistry? eventUpcasterRegistry = null
	)
	{
		_eventNameMapper = eventNameMapper;
		_eventStoreOptions = sqlServerOptions;
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

		_eventIdPrefix = $"e_{_aggregateTypeShortName}_";
		_idempotencyCheckIdPrefix = $"i_{_aggregateTypeShortName}_";
		_snapshotIdPrefix = $"snap_{_aggregateTypeShortName}_";

		var clientOptions = ResolveClientOptions(sqlServerOptions.Value, _aggregateTypeShortName);
		_client = new PostgresEventStoreClient(clientOptions);
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
		await foreach (
			var aggregateId in _client.GetAggregateIdsByTypeAsync(
				_aggregateTypeShortName,
				includeDeleted,
				cancellationToken
			)
		)
			yield return aggregateId;
	}

	Task<StreamVersionData?> GetStreamVersionAsync(
		string aggregateId,
		bool expectedToExist,
		CancellationToken cancellationToken
	) => GetStreamVersionAsync(aggregateId, expectedToExist, null, null, cancellationToken);

	async Task<StreamVersionData?> GetStreamVersionAsync(
		string aggregateId,
		bool expectedToExist,
		NpgsqlConnection? connection,
		NpgsqlTransaction? transaction,
		CancellationToken cancellationToken
	)
	{
		_eventStoreTelemetry.GetStreamVersionStart(aggregateId);

		var elapsedMilliseconds = 0L;
		StreamVersionData? result = null;
		try
		{
			var sw = System.Diagnostics.Stopwatch.StartNew();

			var row = connection is null
				? await _client.GetByIdAsync(CreateStreamVersionId(aggregateId), cancellationToken)
				: await _client.GetByIdAsync(
					CreateStreamVersionId(aggregateId),
					connection,
					transaction,
					cancellationToken
				);
			sw.Stop();

			elapsedMilliseconds = sw.ElapsedMilliseconds;

			if (row == null || row.Value.EntityType != StreamVersionType)
			{
				if (expectedToExist)
					_eventStoreTelemetry.StreamVersionExpectedToExistButNotFound(
						aggregateId,
						_aggregateTypeShortName,
						_aggregateTypeFullName
					);
				else
					_eventStoreTelemetry.StreamVersionNotFound(aggregateId);
			}
			else
			{
				result = new StreamVersionData
				{
					Id = row.Value.Id,
					AggregateId = row.Value.AggregateId,
					AggregateType = row.Value.AggregateType,
					Version = row.Value.Version,
					IsDeleted = row.Value.IsDeleted,
				};
				_eventStoreTelemetry.StreamVersionFound(
					aggregateId,
					result.Version,
					result.AggregateType,
					result.IsDeleted
				);
			}
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

	string CreateEventId(string aggregateId, int version)
	{
		var versionText = version.ToString(CultureInfo.InvariantCulture);
		var padLength = Math.Max(0, _eventStoreOptions.Value.EventSuffixLength - versionText.Length);
		return string.Create(
			_eventIdPrefix.Length + aggregateId.Length + 1 + versionText.Length + padLength,
			(_eventIdPrefix, aggregateId, versionText, padLength),
			static (span, state) =>
			{
				state._eventIdPrefix.AsSpan().CopyTo(span);
				var position = state._eventIdPrefix.Length;
				state.aggregateId.AsSpan().CopyTo(span[position..]);
				position += state.aggregateId.Length;
				span[position] = '_';
				position++;
				span.Slice(position, state.padLength).Fill('0');
				position += state.padLength;
				state.versionText.AsSpan().CopyTo(span[position..]);
			}
		);
	}

	string CreateIdempotencyCheckId(string aggregateId, string idempotencyId) =>
		string.Concat(_idempotencyCheckIdPrefix, aggregateId, "_", idempotencyId);

	string CreateSnapshotId(string aggregateId) => string.Concat(_snapshotIdPrefix, aggregateId);

	/// <summary>
	/// Creates the distributed-cache key for the given aggregate id.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <returns>The cache key used for <paramref name="aggregateId"/>.</returns>
	public string CreateCacheKey(string aggregateId) =>
		$"{_aggregateTypeShortName}:{aggregateId}{AggregateSnapshotSchema.GetStorageSuffix<T>()}".ToUpperInvariant();

	//async Task EnsureConfiguredAsync(CancellationToken cancellationToken)
	//{
	//	if (_eventStoreOptions.Value.AutoCreateTable)
	//		await _client.EnsureTableExistsAsync(cancellationToken);
	//}

	/// <summary>
	/// Merges global options with any per-aggregate-type table override.
	/// Returns an options instance with the effective schema and table name.
	/// </summary>
	static PostgresEventStoreOptions ResolveClientOptions(PostgresEventStoreOptions options, string aggregateTypeName)
	{
		return !options.AggregateTableOverrides.TryGetValue(aggregateTypeName, out var ovr) || ovr is null ? options
			: ovr.SchemaName is null && ovr.TableName is null ? options
			: new PostgresEventStoreOptions
			{
				ConnectionString = options.ConnectionString,
				SchemaName = ovr.SchemaName ?? options.SchemaName,
				TableName = ovr.TableName ?? options.TableName,
				AutoCreateTable = options.AutoCreateTable,
				UseDataCompression = options.UseDataCompression,
				TimeoutInSeconds = options.TimeoutInSeconds,
				MaxEventCountOnSave = options.MaxEventCountOnSave,
				RemoveDeletedFromCache = options.RemoveDeletedFromCache,
				EventSuffixLength = options.EventSuffixLength,
				CacheMode = options.CacheMode,
				DefaultCacheSlidingDuration = options.DefaultCacheSlidingDuration,
				RequiresValidPrincipalIdentifier = options.RequiresValidPrincipalIdentifier,
				JsonIndexOptions = options.JsonIndexOptions,
			};
	}

	internal sealed class StreamVersionData
	{
		public string Id { get; set; } = default!;
		public string AggregateId { get; set; } = default!;
		public string AggregateType { get; set; } = default!;
		public int Version { get; set; }
		public bool IsDeleted { get; set; }
	}
}
