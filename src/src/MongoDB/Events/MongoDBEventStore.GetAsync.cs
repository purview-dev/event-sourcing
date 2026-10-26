using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.MongoDB.Events;
using Purview.EventSourcing.MongoDB.Events.Entities;
using Purview.Results;

namespace Purview.EventSourcing.MongoDB;

partial class MongoDBEventStore<T>
{
	///<inheritdoc/>
	[System.Diagnostics.DebuggerStepThrough]
	public Task<Result<T, AggregateReadFailure>> GetAsync(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => GetCoreAsync(aggregateId, operationContext, cancellationToken);

	async Task<Result<T, AggregateReadFailure>> GetCoreAsync(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId, nameof(aggregateId));

		operationContext ??= EventStoreOperationContext.DefaultContext();

		using var activity = _eventStoreTelemetry.GetAggregate(aggregateId, _aggregateTypeFullName);
		_eventStoreTelemetry.GetAggregateStart(aggregateId, _aggregateTypeFullName);
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

				return ReturnAggregate(aggregate.Details.IsDeleted, operationContext)
					? PrepareAggregateForReturn(aggregate, _aggregateRequirementsManager)
					: Deleted(aggregateId);
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

					return ReturnAggregate(cached.Details.IsDeleted, operationContext)
						? PrepareAggregateForReturn(cached, _aggregateRequirementsManager)
						: Deleted(aggregateId);
				}
			}

			var streamVersion = await GetStreamVersionAsync(aggregateId, true, cancellationToken);
			if (streamVersion == null)
				return NotFound(aggregateId);

			if (!ReturnAggregate(streamVersion.IsDeleted, operationContext))
				return Deleted(aggregateId);

			//** THIS ISN'T CORRECT, SO WE'RE PASSING IN NULL TO ENABLE ALL ADDITIONAL EVENTS **//
			// TODO: It's only odd as in I haven't thought it through completely...

			//long? streamVersionIdentifier = streamVersion.Version;
			//if (streamVersionIdentifier < 1)
			//	// Passing in null for the maxVersion, because we want it to go and find all additional events
			//	// because we don't have a valid version.
			//	streamVersionIdentifier = null;

			// Passing in null for the maxVersion, because we want it to go and find all additional events
			long? streamVersionIdentifier = null;
			if (!operationContext.SkipSnapshot)
			{
				// If there's no snapshot, then this isn't doing a create (as in brand new
				// never before seen object), we know it exists, it has a stream version...
				// This is for the case when there is no snapshot, and we need to create the entity and apply the events.
				aggregate = await GetLatestSnapshotAsync(aggregateId, cancellationToken);
			}

			aggregate ??= new T { Details = { Id = aggregateId } };

			await GetAndApplyEventsAsync(aggregate, streamVersion, streamVersionIdentifier, cancellationToken);
			await UpdateCacheAsync(aggregate, operationContext.CacheOptions, cancellationToken);

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
			_metrics.GetDuration(getStopwatch.Elapsed.TotalMilliseconds, _aggregateTypeShortName);
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
		StreamVersionEntity streamVersion,
		long? maxVersion,
		CancellationToken cancellationToken
	)
	{
		var aggregateId = aggregate.Id();
		var eventCount = 0;
		var everQuery = GetEventRangeAsync(
			aggregateId,
			aggregate.Details.SnapshotVersion + 1,
			maxVersion,
			cancellationToken
		);
		await foreach (var eventResult in everQuery)
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

				// Without doing this, you won't be able to write to this aggregate anymore.
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

		// Activity.Current rather than a parameter: the activity is started by the public operation
		// and this runs inside it.
		_eventStoreTelemetry.EventsReconstituted(
			System.Diagnostics.Activity.Current,
			eventCount,
			aggregate.Details.CurrentVersion
		);
		_eventStoreTelemetry.AggregateLoaded(aggregate.AggregateType);

		aggregate.Details.SavedVersion = aggregate.Details.CurrentVersion;
		aggregate.Details.Etag = streamVersion.Version.ToString(CultureInfo.InvariantCulture);

		// Make sure we set is deleted too, in-case we're being allowed to get deleted aggregates.
		aggregate.Details.IsDeleted = streamVersion.IsDeleted;
	}

	async Task<T?> GetLatestSnapshotAsync(string aggregateId, CancellationToken cancellationToken)
	{
		try
		{
			var snapshot = await _snapshotClient.GetAsync<SnapshotEntity>(
				aggregateId,
				EntityTypes.SnapshotType,
				cancellationToken
			);
			return snapshot == null || snapshot.SchemaVersion != _snapshotSchemaVersion
				? null
				: DeserializeSnapshot(snapshot.Payload);
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
