using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.SqlServer.Events;

partial class SqlServerEventStore<T>
{
	///<inheritdoc/>
	[System.Diagnostics.DebuggerStepThrough]
	public Task<T?> GetAsync(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => GetCoreAsync(aggregateId, operationContext, cancellationToken);

	async Task<T?> GetCoreAsync(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId, nameof(aggregateId));

		operationContext ??= EventStoreOperationContext.DefaultContext();

		_eventStoreTelemetry.GetAggregateStart(aggregateId, _aggregateTypeFullName);
		using var activity = _eventStoreTelemetry.GetAggregate(aggregateId, _aggregateTypeFullName);
		var getStopwatch = System.Diagnostics.Stopwatch.StartNew();
		try
		{
			var aggregate = operationContext.SnapshotCacheMode.HasFlag(SnapshotCachingOptions.GetFromCache)
				? await GetFromCacheAsync(aggregateId, cancellationToken)
				: null;

			if (
				aggregate != null
				&& (
					!operationContext.ValidateCachedSnapshot
					|| await IsCacheHitFreshAsync(aggregate, aggregateId, cancellationToken)
				)
			)
			{
				_eventStoreTelemetry.AggregateRetrievedFromCache(aggregateId, _aggregateTypeFullName);

				return ReturnAggregate(aggregate.Details.IsDeleted, aggregateId, operationContext)
					? PrepareAggregateForReturn(aggregate, _aggregateRequirementsManager)
					: null;
			}

			// Single-flight: when the aggregate will be written back to the cache, serialize
			// rehydration per stream so concurrent first-reads of a cold aggregate do not each
			// replay the stream and stampede the cache.
			await using var singleFlight = operationContext.SnapshotCacheMode.HasFlag(
				SnapshotCachingOptions.StoreInCache
			)
				? await AggregateWriteLock.AcquireAsync(_aggregateTypeShortName, aggregateId, cancellationToken)
				: null;

			// Another caller may have rehydrated and populated the cache while we waited.
			if (singleFlight != null && operationContext.SnapshotCacheMode.HasFlag(SnapshotCachingOptions.GetFromCache))
			{
				var cached = await GetFromCacheAsync(aggregateId, cancellationToken);
				if (
					cached != null
					&& (
						!operationContext.ValidateCachedSnapshot
						|| await IsCacheHitFreshAsync(cached, aggregateId, cancellationToken)
					)
				)
				{
					_eventStoreTelemetry.AggregateRetrievedFromCache(aggregateId, _aggregateTypeFullName);

					return ReturnAggregate(cached.Details.IsDeleted, aggregateId, operationContext)
						? PrepareAggregateForReturn(cached, _aggregateRequirementsManager)
						: null;
				}
			}

			var streamVersion = await GetStreamVersionAsync(aggregateId, true, cancellationToken);
			if (streamVersion == null)
				return null;

			if (!ReturnAggregate(streamVersion.IsDeleted, aggregateId, operationContext))
				return null;

			int? streamVersionIdentifier = null;
			if (!operationContext.SkipSnapshot)
				aggregate = await GetLatestSnapshotAsync(aggregateId, cancellationToken);

			aggregate ??= new T { Details = { Id = aggregateId } };

			await GetAndApplyEventsAsync(aggregate, streamVersion, streamVersionIdentifier, cancellationToken);
			await UpdateCacheAsync(aggregate, operationContext.CacheOptions, cancellationToken);

			_eventStoreTelemetry.AggregateLoaded(aggregate.AggregateType);

			return PrepareAggregateForReturn(aggregate, _aggregateRequirementsManager);
		}
		catch (Exception ex)
		{
			_eventStoreTelemetry.GetAggregateFailed(aggregateId, _aggregateTypeFullName, ex);
			throw;
		}
		finally
		{
			getStopwatch.Stop();
			_eventStoreTelemetry.GetAggregateComplete(
				aggregateId,
				_aggregateTypeFullName,
				getStopwatch.ElapsedMilliseconds
			);
		}

		async Task<bool> IsCacheHitFreshAsync(T aggregate, string aggregateId, CancellationToken cancellationToken)
		{
			var streamVersion = await GetStreamVersionAsync(aggregateId, false, cancellationToken);
			return streamVersion != null && aggregate.Details.CurrentVersion >= streamVersion.Version;
		}

		static T PrepareAggregateForReturn(
			T aggregate,
			Services.IAggregateRequirementsManager aggregateRequirementsManager
		)
		{
			var currentVersion = aggregate.Details.CurrentVersion;

			aggregate.ClearUnsavedEvents();

			aggregate.Details.CurrentVersion = currentVersion;

			aggregateRequirementsManager.Fulfil(aggregate);

			return aggregate;
		}
	}

	async Task GetAndApplyEventsAsync(
		T aggregate,
		StreamVersionData streamVersion,
		int? maxVersion,
		CancellationToken cancellationToken
	)
	{
		var aggregateId = aggregate.Id();
		var eventCount = 0;
		var eventQuery = GetEventRangeAsync(
			aggregateId,
			aggregate.Details.SnapshotVersion + 1,
			maxVersion,
			cancellationToken
		);
		await foreach (var eventResult in eventQuery)
		{
			var eventRecord = eventResult.EventRecord;
			var @event = eventRecord.Event;
			var metadata = eventRecord.Metadata;
			if (@event is UnknownEvent || !aggregate.CanApplyEvent(@event))
			{
				var eventType = @event.GetType();
				if (@event is UnknownEvent)
				{
					_eventStoreTelemetry.SkippedUnknownEvent(
						aggregateId,
						_aggregateTypeFullName,
						aggregate.AggregateType,
						eventResult.EventType,
						metadata.AggregateVersion
					);

					(aggregate as AggregateBase)?.RecordSkippedEvent(
						metadata.AggregateVersion,
						eventResult.EventType,
						isUnknown: true
					);
				}
				else
				{
					_eventStoreTelemetry.CannotApplyEvent(
						aggregateId,
						_aggregateTypeFullName,
						aggregate.AggregateType,
						eventResult.EventType,
						eventType.FullName ?? eventType.Name,
						metadata.AggregateVersion
					);

					(aggregate as AggregateBase)?.RecordSkippedEvent(
						metadata.AggregateVersion,
						eventResult.EventType,
						isUnknown: false
					);
				}

				aggregate.Details.CurrentVersion = metadata.AggregateVersion;
			}
			else
				aggregate.ApplyEvent(@event, metadata);

			eventCount++;
		}

		_eventStoreTelemetry.ReconstitutedAggregateFromEvents(
			aggregateId,
			_aggregateTypeFullName,
			aggregate.AggregateType,
			eventCount,
			AggregateVersionData.Create(aggregate)
		);

		aggregate.Details.SavedVersion = aggregate.Details.CurrentVersion;
		aggregate.Details.Etag = streamVersion.Version.ToString(CultureInfo.InvariantCulture);

		aggregate.Details.IsDeleted = streamVersion.IsDeleted;
	}

	async Task<T?> GetLatestSnapshotAsync(string aggregateId, CancellationToken cancellationToken)
	{
		try
		{
			var snapshotId = CreateSnapshotId(aggregateId);
			var row = await _client.GetByIdAsync(snapshotId, cancellationToken);
			return
				row == null
				|| row.Value.EntityType != SnapshotType
				|| row.Value.SchemaVersion != _snapshotSchemaVersion
				|| string.IsNullOrWhiteSpace(row.Value.Payload)
				? null
				: DeserializeSnapshot(row.Value.Payload);
		}
#pragma warning disable CA1031
		catch (Exception ex)
#pragma warning restore CA1031
		{
			_eventStoreTelemetry.SnapshotDeserializationFailed(aggregateId, _aggregateTypeFullName, ex);

			return null;
		}
	}

	async Task<T?> GetFromCacheAsync(string aggregateId, CancellationToken cancellationToken)
	{
		T? aggregate = null;
		try
		{
			var cacheKey = CreateCacheKey(aggregateId);
			var snapshotData = await _distributedCache.GetStringAsync(cacheKey, cancellationToken);
			if (!string.IsNullOrWhiteSpace(snapshotData))
			{
				aggregate = DeserializeSnapshot(snapshotData);
				aggregate.Details.SavedVersion = aggregate.Details.CurrentVersion;
			}
		}
#pragma warning disable CA1031
		catch (Exception ex)
#pragma warning restore CA1031
		{
			_eventStoreTelemetry.CacheGetFailure(aggregateId, _aggregateTypeFullName, ex);
		}

		return aggregate;
	}
}
