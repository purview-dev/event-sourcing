using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Azure.Data.Tables;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events.Upcasting;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.AzureStorage.Entities;
using Purview.EventSourcing.Services;
using Purview.EventSourcing.Validation;

namespace Purview.EventSourcing.AzureStorage;

/// <summary>
/// An event store for <typeparamref name="T"/> aggregates backed by Azure Table and Blob Storage.
/// </summary>
/// <typeparam name="T">The <see cref="IAggregate"/> type the store persists.</typeparam>
/// <remarks>
/// <para>
/// Events and stream-version entities are persisted to Azure Table Storage, while aggregate snapshots and
/// large event payloads are stored in Azure Blob Storage. Aggregates are reconstituted by replaying their
/// event stream, optionally accelerated through a distributed cache.
/// </para>
/// <para>
/// The store implements <see cref="ITableEventStore{T}"/>, combining the non-queryable event-store contract
/// with the aggregate event-history contract. Register it through
/// <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions"/> or resolve it directly from the service provider.
/// </para>
/// </remarks>
public sealed partial class TableEventStore<T> : ITableEventStore<T>, IAsyncDisposable
	where T : class, IAggregate, new()
{
	readonly StorageClients.Table.AzureTableClient _tableClient;
	readonly StorageClients.Blob.AzureBlobClient _blobClient;

	readonly IAggregateEventNameMapper _eventNameMapper;
	readonly IOptions<AzureStorageEventStoreOptions> _eventStoreOptions;
	readonly IAggregateValidator<T>? _validator;
	readonly IAggregateIdFactory? _aggregateIdFactory;
	readonly IDistributedCache _distributedCache;
	readonly ITableEventStoreTelemetry _eventStoreTelemetry;
	readonly ChangeFeed.IAggregateChangeFeedNotifier<T> _aggregateChangeNotifier;
	readonly IAggregateRequirementsManager _aggregateRequirementsManager;
	readonly ISnapshotStrategy<T> _snapshotStrategy;
	readonly ISnapshotStrategySelector? _snapshotStrategySelector;
	readonly IEventUpcasterRegistry? _eventUpcasterRegistry;

	readonly string _aggregateTypeFullName;
	readonly string _aggregateTypeShortName;
	readonly TableSaveOperation<T> _saveOperation;

	static readonly ConcurrentDictionary<string, Type> EventTypeCache = new(StringComparer.Ordinal);

	/// <summary>
	/// Initializes a new instance of the <see cref="TableEventStore{T}"/> class.
	/// </summary>
	/// <param name="eventNameMapper">Maps between aggregate event types and their persisted names.</param>
	/// <param name="azureStorageOptions">The Azure Storage options used to configure the store.</param>
	/// <param name="distributedCache">The cache used to store and retrieve aggregate snapshots.</param>
	/// <param name="eventStoreTelemetry">The telemetry sink used to record store operations.</param>
	/// <param name="aggregateChangeNotifier">The change-feed notifier invoked around save operations.</param>
	/// <param name="aggregateRequirementsManager">The manager used to fulfil aggregate requirements.</param>
	/// <param name="validator">Optional, the validator used to validate aggregates before saving.</param>
	/// <param name="nameBuilder">Optional, the builder used to generate table and blob container names.</param>
	/// <param name="aggregateIdFactory">Optional, the factory used to generate aggregate ids on create.</param>
	/// <param name="snapshotStrategy">Optional, the strategy that decides when a snapshot should be written.</param>
	/// <param name="snapshotStrategySelector">Optional, the selector used to pick a snapshot strategy.</param>
	/// <param name="eventUpcasterRegistry">Optional, the registry used to upcast persisted events during replay.</param>
	/// <exception cref="ArgumentNullException"><paramref name="azureStorageOptions"/> is <see langword="null"/>.</exception>
	public TableEventStore(
		IAggregateEventNameMapper eventNameMapper,
		[NotNull] IOptions<AzureStorageEventStoreOptions> azureStorageOptions,
		IDistributedCache distributedCache,
		ITableEventStoreTelemetry eventStoreTelemetry,
		ChangeFeed.IAggregateChangeFeedNotifier<T> aggregateChangeNotifier,
		IAggregateRequirementsManager aggregateRequirementsManager,
		IAggregateValidator<T>? validator = null,
		ITableEventStoreStorageNameBuilder? nameBuilder = null,
		IAggregateIdFactory? aggregateIdFactory = null,
		ISnapshotStrategy<T>? snapshotStrategy = null,
		ISnapshotStrategySelector? snapshotStrategySelector = null,
		IEventUpcasterRegistry? eventUpcasterRegistry = null
	)
	{
		_eventNameMapper = eventNameMapper;
		_eventStoreOptions = azureStorageOptions;
		_validator = validator;
		_aggregateIdFactory = aggregateIdFactory;
		_distributedCache = distributedCache;
		_eventStoreTelemetry = eventStoreTelemetry;
		_aggregateChangeNotifier = aggregateChangeNotifier;
		_aggregateRequirementsManager = aggregateRequirementsManager;
		_snapshotStrategy = snapshotStrategy ?? new IntervalSnapshotStrategy<T>();
		_snapshotStrategySelector = snapshotStrategySelector;
		_eventUpcasterRegistry = eventUpcasterRegistry;

		var name = typeof(T).Name;

		TableName = nameBuilder?.GetTableName<T>() ?? $"{azureStorageOptions.Value.Table}{name}";
		ContainerName = nameBuilder?.GetBlobContainerName<T>() ?? azureStorageOptions.Value.Container;

		_tableClient = new(azureStorageOptions.Value, TableName);
		_blobClient = new(azureStorageOptions.Value, ContainerName);

		_aggregateTypeShortName = typeof(T).Name;
		_aggregateTypeFullName = typeof(T).FullName ?? _aggregateTypeShortName;

		var aggregateName = _eventNameMapper.InitializeAggregate<T>();
		if (!aggregateName.Contains('.', StringComparison.InvariantCulture))
			// Could do with validating that this is a valid blob container name.
			_aggregateTypeShortName = aggregateName;

		_saveOperation = new TableSaveOperation<T>(
			this,
			_tableClient,
			_blobClient,
			_eventNameMapper,
			_eventStoreOptions,
			_validator,
			_aggregateChangeNotifier,
			_eventStoreTelemetry,
			_aggregateTypeFullName,
			_snapshotStrategy,
			_snapshotStrategySelector
		);
	}

	internal string TableName { get; }

	internal string ContainerName { get; }

	///<inheritdoc/>
	public T FulfilRequirements(T aggregate)
	{
		_aggregateRequirementsManager.Fulfil(aggregate);

		return aggregate;
	}

	internal async Task UpdateCacheAsync(
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
		List<string> tableColumns = [nameof(ITableEntity.PartitionKey)];
		if (!includeDeleted)
			tableColumns.Add(nameof(StreamVersionEntity.IsDeleted));

		var query = _tableClient.QueryEnumerableAsync<StreamVersionEntity>(
			m => m.RowKey == TableEventStoreConstants.StreamVersionRowKey,
			fields: tableColumns,
			cancellationToken: cancellationToken
		);
		await foreach (var entity in query)
		{
			if (includeDeleted || !entity.IsDeleted)
				yield return entity.PartitionKey;
		}
	}

	internal async Task<StreamVersionEntity?> GetStreamVersionAsync(
		string aggregateId,
		bool expectedToExist,
		CancellationToken cancellationToken
	)
	{
		_eventStoreTelemetry.GetStreamVersionStart(aggregateId, TableEventStoreConstants.StreamVersionRowKey);

		var elapsedMilliseconds = 0L;
		StreamVersionEntity? result = null;
		try
		{
			var sw = System.Diagnostics.Stopwatch.StartNew();

			result = await _tableClient.GetAsync<StreamVersionEntity>(
				aggregateId,
				TableEventStoreConstants.StreamVersionRowKey,
				cancellationToken
			);
			sw.Stop();

			elapsedMilliseconds = sw.ElapsedMilliseconds;

			if (result == null)
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
			_eventStoreTelemetry.GetStreamVersionFailed(aggregateId, TableEventStoreConstants.StreamVersionRowKey, ex);
		}

		_eventStoreTelemetry.GetStreamVersionComplete(
			aggregateId,
			TableEventStoreConstants.StreamVersionRowKey,
			elapsedMilliseconds
		);

		return result;
	}

	internal void ClearCacheFireAndForget(T aggregate)
	{
		Task.Run(async () =>
		{
			try
			{
				var cacheKey = CreateCacheKey(aggregate.Id());
				// Do not pass in the cancellation token. We want this to carry on as long as possible.
				await _distributedCache.RemoveAsync(cacheKey);
			}
#pragma warning disable CA1031
			catch (Exception ex)
#pragma warning restore CA1031
			{
				_eventStoreTelemetry.CacheRemovalFailure(aggregate.Id(), _aggregateTypeFullName, ex);
			}
		});
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

	internal string CreateEventRowKey(int version) =>
		$"{_eventStoreOptions.Value.EventPrefix}_{$"{version}".PadLeft(_eventStoreOptions.Value.EventSuffixLength, '0')}";

	internal static string CreateIdempotencyCheckRowKey(string idempotencyId) =>
		$"{TableEventStoreConstants.IdempotencyCheckRowKeyPrefix}{idempotencyId}";

#pragma warning disable CA1308 // Normalize strings to uppercase
	internal string GenerateEventBlobName(string aggregateId, string eventId) =>
		$"{_aggregateTypeShortName}/{aggregateId}/{eventId}.json".ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

#pragma warning disable CA1308 // Normalize strings to uppercase
	/// <summary>
	/// Generates the name of the blob that stores the aggregate's snapshot.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <returns>The blob name, including the snapshot file name.</returns>
	/// <seealso cref="GenerateSnapshotBlobPath"/>
	/// <seealso cref="CreateCacheKey"/>
	/// <exception cref="ArgumentException"><paramref name="aggregateId"/> is null, empty, or white space.</exception>
	public string GenerateSnapshotBlobName(string aggregateId)
	{
		var schemaVersion = AggregateSnapshotSchema.GetVersion<T>();
		var fileName =
			schemaVersion == 1 ? TableEventStoreConstants.SnapshotFilename : $"snapshot-sv{schemaVersion}.json";
		return $"{GenerateSnapshotBlobPath(aggregateId)}/{fileName}".ToLowerInvariant();
	}
#pragma warning restore CA1308 // Normalize strings to uppercase

#pragma warning disable CA1308 // Normalize strings to uppercase
	/// <summary>
	/// Generates the blob path (folder) under which the aggregate's snapshot and large events are stored.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <returns>The blob path, excluding the file name.</returns>
	/// <seealso cref="GenerateSnapshotBlobName"/>
	/// <seealso cref="CreateCacheKey"/>
	/// <exception cref="ArgumentException"><paramref name="aggregateId"/> is null, empty, or white space.</exception>
	public string GenerateSnapshotBlobPath(string aggregateId) =>
		$"{_aggregateTypeShortName}/{aggregateId}".ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

#pragma warning disable CA1308 // Normalize strings to uppercase
	/// <summary>
	/// Creates the cache key used to store and retrieve the aggregate in the distributed cache.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <returns>The cache key for the aggregate.</returns>
	/// <seealso cref="GenerateSnapshotBlobName"/>
	/// <seealso cref="GenerateSnapshotBlobPath"/>
	/// <exception cref="ArgumentException"><paramref name="aggregateId"/> is null, empty, or white space.</exception>
	public string CreateCacheKey(string aggregateId) =>
		$"{_aggregateTypeShortName}:{aggregateId}{AggregateSnapshotSchema.GetStorageSuffix<T>()}".ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

	///<inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await _tableClient.DisposeAsync();
		await _blobClient.DisposeAsync();
	}
}
