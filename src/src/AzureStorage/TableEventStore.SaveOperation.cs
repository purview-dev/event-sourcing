using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.AzureStorage.Entities;
using Purview.EventSourcing.AzureStorage.Events;
using Purview.EventSourcing.AzureStorage.StorageClients.Blob;
using Purview.EventSourcing.AzureStorage.StorageClients.Table;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.Serialization;
using Purview.EventSourcing.Services;
using Purview.EventSourcing.Validation;
using Purview.Results;

namespace Purview.EventSourcing.AzureStorage;

sealed class TableSaveOperation<T>(
	TableEventStore<T> store,
	AzureTableClient tableClient,
	AzureBlobClient blobClient,
	IAggregateEventNameMapper eventNameMapper,
	IOptions<AzureStorageEventStoreOptions> eventStoreOptions,
	IAggregateValidator<T>? validator,
	ChangeFeed.IAggregateChangeFeedNotifier<T> aggregateChangeNotifier,
	ITableEventStoreTelemetry eventStoreTelemetry,
	string aggregateTypeFullName,
	ISnapshotStrategy<T> snapshotStrategy,
	ISnapshotStrategySelector? snapshotStrategySelector,
	IEventStorePrincipalAccessor principalAccessor,
	IEventStoreMetrics metrics,
	IEventPayloadProtector? payloadProtector
)
	where T : class, IAggregate, new()
{
	const int SerializationBufferSize = 4096;
	const int MaxEventSize = 32000;

	[DebuggerStepThrough]
	public Task<Result<SaveOutcome<T>, AggregateSaveFailure>> SaveAsync(
		[NotNull] T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => SaveCoreAsync(aggregate, operationContext, cancellationToken);

	internal async Task<Result<SaveOutcome<T>, AggregateSaveFailure>> SaveCoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken,
		params EventRecord[] additionalEvents
	)
	{
		var saveStopwatch = System.Diagnostics.Stopwatch.StartNew();
		var (saveResult, preparedAggregate, preparedOperationContext, idempotencyId, changeEvents, isNew, marker) =
			await PrepareSaveAsync(aggregate, operationContext, additionalEvents, cancellationToken);

		var result =
			saveResult
			?? await PersistAndNotifyAsync(
				preparedAggregate,
				preparedOperationContext,
				idempotencyId,
				changeEvents,
				isNew,
				marker!,
				cancellationToken
			);

		saveStopwatch.Stop();
		metrics.RecordSave(saveStopwatch.Elapsed.TotalMilliseconds, store.AggregateTypeShortName, result);

		// Only a write actually appended events; a no-op or an idempotent replay appended none.
		if (result.IsSuccess && result.Value.Kind is SaveOutcomeKind.Written)
			metrics.EventsAppended(changeEvents.Length, store.AggregateTypeShortName);

		return result;
	}

	async Task<(
		Result<SaveOutcome<T>, AggregateSaveFailure>? Terminal,
		T Aggregate,
		EventStoreOperationContext OperationContext,
		string IdempotencyId,
		EventRecord[] ChangeEvents,
		bool IsNew,
		IdempotencyMarkerEntity? Marker
	)> PrepareSaveAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		EventRecord[]? additionalEvents,
		CancellationToken cancellationToken
	)
	{
		operationContext ??= EventStoreOperationContext.DefaultContext();

		store.FulfilRequirements(aggregate);

		var idempotencyId = operationContext.CorrelationId ?? Activity.Current?.Id ?? $"{Guid.NewGuid()}";
		var validationResult = await GuardAsync(aggregate, cancellationToken);

		if (!validationResult.IsValid)
			return (
				AggregateSaveFailure.Failure<SaveOutcome<T>>(new AggregateInvalid(validationResult)),
				aggregate,
				operationContext,
				idempotencyId,
				[],
				false,
				null
			);

		if (aggregate.Details.Locked)
		{
			return (
				AggregateSaveFailure.Failure<SaveOutcome<T>>(
					new AggregateLocked(aggregate.AggregateType, aggregate.Id())
				),
				aggregate,
				operationContext,
				idempotencyId,
				[],
				false,
				null
			);
		}

		if (string.IsNullOrWhiteSpace(aggregate.Details.Id))
			throw new Exceptions.MissingAggregateIdException(idempotencyId);

		eventStoreTelemetry.SaveCalled(aggregate.Id(), aggregateTypeFullName, aggregate.AggregateType);
		if (!aggregate.HasUnsavedEvents() && (additionalEvents?.Length ?? 0) == 0)
		{
			eventStoreTelemetry.SaveContainedNoChanges(aggregate.Id(), aggregateTypeFullName, aggregate.AggregateType);

			return (
				new SaveOutcome<T>(aggregate, SaveOutcomeKind.NoChanges),
				aggregate,
				operationContext,
				idempotencyId,
				[],
				false,
				null
			);
		}

		var isNew = aggregate.IsNew();
		var changeEvents = aggregate.GetUnsavedEvents().Concat(additionalEvents ?? []).ToArray();
		var idempotencyMarkerOperation = CreateIdempotencyMarkerOperation(aggregate, idempotencyId, changeEvents);

		if (changeEvents.Length > eventStoreOptions.Value.MaxEventCountOnSave)
			throw new ArgumentOutOfRangeException(
				$"The maximum amount of events to save was exceeded. Attempted: {changeEvents.Length}, Maximum: {eventStoreOptions.Value.MaxEventCountOnSave}"
			);

		if (operationContext.UseIdempotencyMarker)
		{
			var exists = await tableClient.EntityExistsAsync(
				idempotencyMarkerOperation.PartitionKey,
				idempotencyMarkerOperation.RowKey,
				cancellationToken
			);
			if (exists)
			{
				eventStoreTelemetry.EventsAlreadyApplied(aggregate.Id(), idempotencyId);
				return (
					new SaveOutcome<T>(aggregate, SaveOutcomeKind.AlreadyApplied),
					aggregate,
					operationContext,
					idempotencyId,
					changeEvents,
					isNew,
					idempotencyMarkerOperation
				);
			}
		}

		return (null, aggregate, operationContext, idempotencyId, changeEvents, isNew, idempotencyMarkerOperation);
	}

	[SuppressMessage(
		"Maintainability",
		"CA1502:Avoid excessive complexity",
		Justification = "Save orchestration handles many interleaved states; keep the flow readable."
	)]
	async Task<Result<SaveOutcome<T>, AggregateSaveFailure>> PersistAndNotifyAsync(
		T aggregate,
		EventStoreOperationContext operationContext,
		string idempotencyId,
		EventRecord[] changeEvents,
		bool isNew,
		IdempotencyMarkerEntity idempotencyMarkerOperation,
		CancellationToken cancellationToken
	)
	{
		if (
			operationContext.NotificationMode.HasFlag(NotificationModes.BeforeDelete)
			&& changeEvents.Any(record => record.Event is Deleted)
		)
			await aggregateChangeNotifier.BeforeDeleteAsync(aggregate, cancellationToken);
		else if (operationContext.NotificationMode.HasFlag(NotificationModes.BeforeSave))
			await aggregateChangeNotifier.BeforeSaveAsync(aggregate, isNew, cancellationToken);

		var streamEntity = await store.GetStreamVersionAsync(aggregate.Id(), !isNew, cancellationToken);
		var hasStreamEntity = streamEntity != null;
		if (streamEntity?.IsDeleted == true && !changeEvents.Any(record => record.Event is Restored))
		{
			return AggregateSaveFailure.Failure<SaveOutcome<T>>(
				new AggregateDeleted(aggregate.AggregateType, aggregate.Id())
			);
		}

		try
		{
			var previousAggregateVersion = aggregate.Details.SavedVersion;
			var shouldSnapshot = ShouldSnapShot(aggregate, changeEvents, operationContext);
			BatchOperation batchOperation = new();
			streamEntity = new()
			{
				PartitionKey = aggregate.Id(),
				RowKey = TableEventStoreConstants.StreamVersionRowKey,
				ETag = streamEntity?.ETag ?? ETag.All,
				IsDeleted = aggregate.Details.IsDeleted,
				AggregateType = aggregate.AggregateType,
				Version = aggregate.Details.CurrentVersion,
			};

			if (isNew || !hasStreamEntity)
				batchOperation.Add(streamEntity);
			else
				batchOperation.Update(streamEntity, merge: false);

			var userId = principalAccessor.GetPrincipal()?.FindFirst(operationContext.ClaimIdentifier)?.Value;
			if (operationContext.RequiresValidPrincipalIdentifier && string.IsNullOrWhiteSpace(userId))
				throw new NullReferenceException(
					$"Missing ClaimsPrincipal identifier '{operationContext.ClaimIdentifier}'. Unable to save aggregate."
				);

			var idempotencyIdAsString = idempotencyId.ToUpperInvariant();
			Dictionary<string, object> largeChangeEvents = [];
			for (var i = 0; i < changeEvents.Length; i++)
			{
				var changeEvent = changeEvents[i].Event;
				var metadata = changeEvents[i].Metadata with
				{
					IdempotencyId = idempotencyIdAsString,
					SchemaVersion = changeEvents[i].Metadata.SchemaVersion,
					UserId = userId,
					CorrelationId = changeEvents[i].Metadata.CorrelationId ?? operationContext.CorrelationId,
				};

				await AppendEventToBatchAsync(
					changeEvent,
					metadata,
					aggregate,
					idempotencyMarkerOperation,
					batchOperation,
					largeChangeEvents,
					cancellationToken
				);
			}

			if (operationContext.UseIdempotencyMarker)
				batchOperation.Add(idempotencyMarkerOperation, recordAt: 0);

			await SubmitBatchOperationsAsync(
				aggregate,
				idempotencyId,
				batchOperation,
				operationContext.UseIdempotencyMarker,
				cancellationToken
			);

			if (largeChangeEvents.Count > 0)
			{
				// Always snapshot when there's a large event.
				shouldSnapshot = true;

				await WriteLargeEventEntitiesAsync(
					aggregate,
					[.. largeChangeEvents],
					idempotencyId,
					idempotencyMarkerOperation.RowKey,
					cancellationToken
				);
			}

			// We create a snapshot if it's been deleted or restored, could make searching easier later on.
			if (shouldSnapshot)
				await CreateSnapshotAsync(aggregate, cancellationToken);

			if (changeEvents.Any(record => record.Event is Deleted))
				eventStoreTelemetry.AggregateDeleted(aggregate.Id(), aggregateTypeFullName, aggregate.AggregateType);
			else if (changeEvents.Any(record => record.Event is Restored))
				eventStoreTelemetry.AggregateRestored(aggregate.Id(), aggregateTypeFullName, aggregate.AggregateType);

			eventStoreTelemetry.SavedAggregate(
				aggregate.Id(),
				aggregateTypeFullName,
				changeEvents.Length,
				aggregate.AggregateType
			);

			// Do not pass in the cancellation token. We want this to carry on as long as possible.
			await store.UpdateCacheAsync(aggregate, operationContext.CacheOptions, cancellationToken);

			// ...or here.
			if (aggregate.Details.IsDeleted && operationContext.NotificationMode.HasFlag(NotificationModes.AfterDelete))
				await aggregateChangeNotifier.AfterDeleteAsync(aggregate, cancellationToken);
			else if (operationContext.NotificationMode.HasFlag(NotificationModes.AfterSave))
				await aggregateChangeNotifier.AfterSaveAsync(
					aggregate,
					previousAggregateVersion,
					isNew,
					changeEvents,
					cancellationToken
				);
		}
		catch (Exception ex)
		{
			store.ClearCacheFireAndForget(aggregate);

			if (operationContext.NotificationMode.HasFlag(NotificationModes.OnFailure))
			{
				var deleteRequested = changeEvents.Any(record => record.Event is Deleted);
				await aggregateChangeNotifier.FailureAsync(aggregate, deleteRequested, ex, cancellationToken);
			}

			if (ex is Exceptions.ConcurrencyException conflict)
			{
				return AggregateSaveFailure.Failure<SaveOutcome<T>>(
					new ConcurrencyConflict(
						aggregate.AggregateType,
						aggregate.Id(),
						conflict.VersionAttempted,
						conflict.Version
					)
				);
			}

			throw;
		}

		return new SaveOutcome<T>(aggregate, SaveOutcomeKind.Written);
	}

	async Task AppendEventToBatchAsync(
		object changeEvent,
		EventMetadata metadata,
		T aggregate,
		IdempotencyMarkerEntity idempotencyMarkerOperation,
		BatchOperation batchOperation,
		Dictionary<string, object> largeChangeEvents,
		CancellationToken cancellationToken
	)
	{
		var serializedEvent = await ProtectIfRequiredAsync(
			changeEvent,
			TableEventStore<T>.SerializeEvent(changeEvent),
			cancellationToken
		);
		var eventEntity = CreateSerializedEvent(
			aggregate.Id(),
			changeEvent,
			metadata,
			serializedEvent,
			idempotencyMarkerOperation.RowKey
		);
		if (Encoding.UTF8.GetByteCount(serializedEvent) >= MaxEventSize)
		{
			LargeEventPointerEvent largeEventPointer = new()
			{
				SerializedEventType = eventNameMapper.GetName<T>(changeEvent),
			};
			var serializedEventPointer = CreateSerializedEvent(
				aggregate.Id(),
				changeEvent,
				metadata,
				TableEventStore<T>.SerializeEvent(largeEventPointer),
				idempotencyMarkerOperation.RowKey
			);

			serializedEventPointer.EventType = eventNameMapper.GetName<T>(largeEventPointer);

			batchOperation.Add(serializedEventPointer);
			largeChangeEvents.Add(eventEntity.RowKey, changeEvent);
		}
		else
			batchOperation.Add(eventEntity);
	}

	async Task SubmitBatchOperationsAsync(
		T aggregate,
		string idempotencyId,
		BatchOperation batchOperation,
		bool useIdempotencyMarker,
		CancellationToken cancellationToken
	)
	{
		try
		{
			// idx 0: IdempotencyMarker - Add
			// idx 1: StreamEntity - Add or Update (merge: false)
			// idx x: Events - Add

			var batchResults = await tableClient.SubmitBatchAsync(batchOperation, cancellationToken);

			aggregate.Details.Etag = batchResults.Responses[useIdempotencyMarker ? 1 : 0].Headers.ETag?.ToString();

			var currentVersion = aggregate.Details.CurrentVersion;

			aggregate.ClearUnsavedEvents();

			aggregate.Details.CurrentVersion = aggregate.Details.SavedVersion = currentVersion;
		}
		catch (RequestFailedException ex)
		{
			eventStoreTelemetry.SaveFailedAtStorage(aggregate.Id(), aggregateTypeFullName, ex.Status, ex);

			// Only a transaction failure identifies which action collided, and the classification
			// below is meaningless without it.
			batchOperation.RecordFailedAction((ex as TableTransactionFailedException)?.FailedTransactionActionIndex);

			var statusCode = (HttpStatusCode)ex.Status;

			store.ClearCacheFireAndForget(aggregate);

			if (statusCode == HttpStatusCode.PreconditionFailed)
				throw new Exceptions.ConcurrencyException(
					aggregate.Id(),
					idempotencyId,
					aggregate.Details.CurrentVersion,
					aggregate.Details.SavedVersion
				);

			if (statusCode == HttpStatusCode.Conflict)
			{
				var errorEntity = batchOperation.FailedEntity;
				if (errorEntity != null)
				{
					if (
						errorEntity.RowKey.StartsWith(
							TableEventStoreConstants.IdempotencyCheckRowKeyPrefix,
							StringComparison.Ordinal
						)
					)
						// Idempotency marker already exists, that means transaction with this idempotencyId already succeeded, so we don't care anymore
						return;

					// Either the stream-version row lost its ETag check, or an event row already
					// exists for this version. Event rows are keyed by aggregate version, so a
					// collision means another writer already persisted it - both are the same
					// concurrency conflict, and reporting the second as a commit failure would
					// present a retryable race as an infrastructure fault.
					throw new Exceptions.ConcurrencyException(
						aggregate.Id(),
						idempotencyId,
						aggregate.Details.CurrentVersion,
						aggregate.Details.SavedVersion
					);
				}
			}

			throw new Exceptions.CommitException(
				ex.Status,
				aggregate.Id(),
				idempotencyId,
				aggregate.Details.CurrentVersion,
				aggregate.Details.SavedVersion,
				ex.Message
			);
		}
		catch (Exception ex)
		{
			eventStoreTelemetry.SaveFailed(aggregate.Id(), aggregateTypeFullName, ex);

			store.ClearCacheFireAndForget(aggregate);

			throw;
		}
	}

	async Task CreateSnapshotAsync(T aggregate, CancellationToken cancellationToken)
	{
		// Set the snapshot version to the current version...
		aggregate.Details.SnapshotVersion = aggregate.Details.CurrentVersion;

		var snapshot = TableEventStore<T>.SerializeSnapshot(aggregate);
		var snapshotName = store.GenerateSnapshotBlobName(aggregate.Id());

		using MemoryStream content = new();
		using (StreamWriter writer = new(content, Encoding.UTF8, SerializationBufferSize, leaveOpen: true))
			await writer.WriteAsync(snapshot);

		content.Position = 0;

		await blobClient.UploadAsync(snapshotName, content, overwrite: true, cancellationToken: cancellationToken);
	}

	EventEntity CreateSerializedEvent(
		string aggregateId,
		object @event,
		EventMetadata metadata,
		string serializedEvent,
		string compoundIdempotencyId
	) =>
		new()
		{
			PartitionKey = aggregateId,
			RowKey = store.CreateEventRowKey(metadata.AggregateVersion),
			Payload = serializedEvent,
			EventType = eventNameMapper.GetName<T>(@event),
			IdempotencyId = compoundIdempotencyId,
			SchemaVersion = metadata.SchemaVersion,
			CorrelationId = metadata.CorrelationId,
			CausationId = metadata.CausationId,
			UserId = metadata.UserId,
		};

	async Task WriteLargeEventEntitiesAsync(
		T aggregate,
		KeyValuePair<string, object>[] largeChangeEvents,
		string idempotencyId,
		string compoundIdempotencyId,
		CancellationToken cancellationToken
	)
	{
		var aggregateId = aggregate.Id();
		for (var i = 0; i < largeChangeEvents.Length; i++)
		{
			var largeEvent = largeChangeEvents[i];
			var blobName = store.GenerateEventBlobName(aggregateId, largeEvent.Key);
			var largeEventContent = await ProtectIfRequiredAsync(
				largeEvent.Value,
				TableEventStore<T>.SerializeEvent(largeEvent.Value),
				cancellationToken
			);
			using MemoryStream stream = new();
			using (StreamWriter writer = new(stream, Encoding.UTF8, SerializationBufferSize, true))
				await writer.WriteAsync(largeEventContent);

			stream.Position = 0;

			await blobClient.UploadAsync(
				blobName,
				stream,
				metadata: new Dictionary<string, string>
				{
					{ "AggregateId", aggregateId },
					{ "EventType", eventNameMapper.GetName<T>(largeEvent.Value) },
					{ "IdempotencyId", idempotencyId },
					{ "CompoundIdempotencyId", compoundIdempotencyId },
				},
				overwrite: true,
				cancellationToken: cancellationToken
			);

			eventStoreTelemetry.WritingLargeEvent(
				aggregateId,
				blobName,
				stream.Length,
				largeEvent.Value.GetType().FullName ?? largeEvent.Value.GetType().Name
			);
		}
	}

	static IdempotencyMarkerEntity CreateIdempotencyMarkerOperation(
		T aggregate,
		string idempotencyId,
		EventRecord[] changeEvents
	)
	{
		var compoundIdempotencyId = GenerateIdempotencyId(idempotencyId, changeEvents);
		var rowKey = TableEventStore<T>.CreateIdempotencyCheckRowKey(compoundIdempotencyId);
		IdempotencyMarkerEntity marker = new(aggregate.Id(), rowKey);
		IdempotencyMarkerEventPayload eventObject = new()
		{
			EventIds = [.. changeEvents.Select(m => m.Metadata.AggregateVersion).OrderBy(m => m)],
		};

		marker.Events = EventStoreSerializationHelpers.Serialize(eventObject);

		return marker;
	}

	static string GenerateIdempotencyId(string idempotencyId, EventRecord[] changeEvents)
	{
		HashCode hash = new();
		for (var i = 0; i < changeEvents.Length; i++)
		{
			var @event = changeEvents[i].Event;
			hash.Add(@event);
		}

		return $"{idempotencyId}_{hash.ToHashCode()}";
	}

	bool ShouldSnapShot(T aggregate, EventRecord[] events, EventStoreOperationContext? operationContext)
	{
		// A snapshot serializes aggregate state, which holds the same personal data as the protected
		// events in clear - and one snapshot can span many subjects, so no single key can protect it.
		// With protection enabled the store does not snapshot at all: a snapshot surviving key erasure
		// would make crypto-shredding untrue. Replay from events stays correct, only slower.
		if (payloadProtector is not null)
			return false;

		if (aggregate.Details.IsDeleted || events.Any(record => record.Event is Restored))
			return true;

		// If the aggregate hasn't been deleted or restored, run the strategy to
		// find out if we should snapshot or not.
		return SnapshotStrategyResolver.ShouldSnapshot(
			aggregate,
			events.Length,
			operationContext,
			snapshotStrategy,
			snapshotStrategySelector
		);
	}

	async Task<ValidationResult> GuardAsync(T aggregate, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		return validator == null
			? await DefaultAggregateValidator<T>.Instance.ValidateAsync(aggregate, cancellationToken)
			: await validator.ValidateAsync(aggregate, cancellationToken);
	}

	/// <summary>
	/// Encrypts a serialized payload when its event names a data subject.
	/// </summary>
	/// <remarks>
	/// Applied to both the table payload and the blob used for oversized events, so routing an event to
	/// blob storage does not quietly store it in clear.
	/// </remarks>
	Task<string> ProtectIfRequiredAsync(object changeEvent, string serialized, CancellationToken cancellationToken) =>
		payloadProtector is not null && changeEvent is IEventDataSubject dataSubject
			? payloadProtector.ProtectAsync(serialized, dataSubject.DataSubjectId, cancellationToken)
			: Task.FromResult(serialized);
}
