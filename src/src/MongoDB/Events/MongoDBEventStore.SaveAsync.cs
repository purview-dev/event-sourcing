using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MongoDB.Driver;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.Exceptions;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.MongoDB.Events.Entities;
using Purview.EventSourcing.MongoDB.StorageClient;
using Purview.EventSourcing.Validation;
using Purview.Results;

namespace Purview.EventSourcing.MongoDB;

partial class MongoDBEventStore<T>
{
	///<inheritdoc/>
	[DebuggerStepThrough]
	public Task<Result<SaveOutcome<T>, AggregateSaveFailure>> SaveAsync(
		[NotNull] T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => SaveCoreAsync(aggregate, operationContext, cancellationToken);

	async Task<Result<SaveOutcome<T>, AggregateSaveFailure>> SaveCoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken,
		params EventRecord[] additionalEvents
	)
	{
		using var activity = _eventStoreTelemetry.SaveAggregate(aggregate.Id(), _aggregateTypeFullName);

		var saveStopwatch = System.Diagnostics.Stopwatch.StartNew();
		var preparation = await PrepareSaveAsync(aggregate, operationContext, additionalEvents, cancellationToken);
		var result = preparation.Terminal ?? await PersistAndNotifyAsync(preparation, cancellationToken);

		saveStopwatch.Stop();
		_metrics.RecordSave(saveStopwatch.Elapsed.TotalMilliseconds, _aggregateTypeShortName, result);

		// Only a write actually appended events; a no-op or an idempotent replay appended none.
		if (result.IsSuccess && result.Value.Kind is SaveOutcomeKind.Written)
			_metrics.EventsAppended(preparation.ChangeEvents.Length, _aggregateTypeShortName);
		_eventStoreTelemetry.SaveCompleted(activity, preparation.ChangeEvents.Length);
		_eventStoreTelemetry.AggregateSaved(aggregate.AggregateType);

		return result;
	}

	async Task<MongoSavePreparation> PrepareSaveAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		EventRecord[]? additionalEvents,
		CancellationToken cancellationToken
	)
	{
		operationContext ??= EventStoreOperationContext.DefaultContext();

		FulfilRequirements(aggregate);

		var idempotencyId = operationContext.CorrelationId ?? Activity.Current?.Id ?? $"{Guid.NewGuid()}";
		var validationResult = await GuardAsync(aggregate, cancellationToken);

		if (!validationResult.IsValid)
			return new MongoSavePreparation(
				AggregateSaveFailure.Failure<SaveOutcome<T>>(new AggregateInvalid(validationResult)),
				aggregate,
				operationContext,
				idempotencyId,
				[],
				isNew: false,
				marker: null
			);

		if (aggregate.Details.Locked)
		{
			return new MongoSavePreparation(
				AggregateSaveFailure.Failure<SaveOutcome<T>>(
					new AggregateLocked(aggregate.AggregateType, aggregate.Id())
				),
				aggregate,
				operationContext,
				idempotencyId,
				[],
				isNew: false,
				marker: null
			);
		}

		if (string.IsNullOrWhiteSpace(aggregate.Details.Id))
			throw new MissingAggregateIdException(idempotencyId);

		_eventStoreTelemetry.SaveCalled(aggregate.Id(), _aggregateTypeFullName, aggregate.AggregateType);
		if (!aggregate.HasUnsavedEvents() && (additionalEvents?.Length ?? 0) == 0)
		{
			_eventStoreTelemetry.SaveContainedNoChanges(
				aggregate.Id(),
				_aggregateTypeFullName,
				aggregate.AggregateType
			);

			return new MongoSavePreparation(
				new SaveOutcome<T>(aggregate, SaveOutcomeKind.NoChanges),
				aggregate,
				operationContext,
				idempotencyId,
				[],
				isNew: false,
				marker: null
			);
		}

		var isNew = aggregate.IsNew();
		var changeEvents = aggregate.GetUnsavedEvents().Concat(additionalEvents ?? []).ToArray();
		var idempotencyMarkerOperation = CreateIdempotencyMarkerOperation(aggregate, idempotencyId, changeEvents);

		if (changeEvents.Length > _eventStoreOptions.Value.MaxEventCountOnSave)
			throw new ArgumentOutOfRangeException(
				$"The maximum amount of events to save was exceeded. Attempted: {changeEvents.Length}, Maximum: {_eventStoreOptions.Value.MaxEventCountOnSave}"
			);

		if (operationContext.UseIdempotencyMarker)
		{
			var exists =
				(
					await _eventClient.GetAsync<IdempotencyMarkerEntity>(
						idempotencyMarkerOperation.Id,
						EntityTypes.IdempotencyMarkerType,
						cancellationToken
					)
				) != null;
			if (exists)
			{
				_eventStoreTelemetry.EventsAlreadyApplied(aggregate.Id(), idempotencyId);
				return new MongoSavePreparation(
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

		return new MongoSavePreparation(
			terminal: null,
			aggregate,
			operationContext,
			idempotencyId,
			changeEvents,
			isNew,
			idempotencyMarkerOperation
		);
	}

	async Task<Result<SaveOutcome<T>, AggregateSaveFailure>> PersistAndNotifyAsync(
		MongoSavePreparation preparation,
		CancellationToken cancellationToken
	)
	{
		var aggregate = preparation.Aggregate;
		var operationContext = preparation.OperationContext;
		var idempotencyId = preparation.IdempotencyId;
		var changeEvents = preparation.ChangeEvents;
		var isNew = preparation.IsNew;
		var idempotencyMarkerOperation = preparation.Marker!;

		if (
			operationContext.NotificationMode.HasFlag(NotificationModes.BeforeDelete)
			&& changeEvents.Any(record => record.Event is Deleted)
		)
			await _aggregateChangeNotifier.BeforeDeleteAsync(aggregate, cancellationToken);
		else if (operationContext.NotificationMode.HasFlag(NotificationModes.BeforeSave))
			await _aggregateChangeNotifier.BeforeSaveAsync(aggregate, isNew, cancellationToken);

		var streamEntity = await GetStreamVersionAsync(aggregate.Id(), !isNew, cancellationToken);
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
			var shouldSnapshot = ShouldSnapShot(aggregate, changeEvents);

			BatchOperation batchOperation = new();
			streamEntity = new()
			{
				Id = streamEntity?.Id ?? CreateStreamVersionId(aggregate.Id()),
				AggregateId = aggregate.Id(),
				IsDeleted = aggregate.Details.IsDeleted,
				AggregateType = aggregate.AggregateType,
				Version = aggregate.Details.CurrentVersion,
				Timestamp = DateTimeOffset.UtcNow,
			};

			if (isNew || !hasStreamEntity)
				batchOperation.Insert(streamEntity);
			else
				batchOperation.Update(streamEntity);

			var userId = _principalAccessor.GetPrincipal()?.FindFirst(operationContext.ClaimIdentifier)?.Value;
			if (operationContext.RequiresValidPrincipalIdentifier && string.IsNullOrWhiteSpace(userId))
				throw new NullReferenceException(
					$"Missing ClaimsPrincipal identifier '{operationContext.ClaimIdentifier}'. Unable to save aggregate."
				);

			var idempotencyIdAsString = idempotencyId.ToUpperInvariant();
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

				var serializedEvent = await ProtectIfRequiredAsync(
					changeEvent,
					SerializeEvent(changeEvent),
					cancellationToken
				);
				var eventEntity = CreateSerializedEvent(
					aggregate.Id(),
					changeEvent,
					serializedEvent,
					metadata,
					idempotencyMarkerOperation.Id
				);

				batchOperation.Insert(eventEntity);
			}

			if (operationContext.UseIdempotencyMarker)
				batchOperation.Insert(idempotencyMarkerOperation);

			await SubmitBatchOperationsAsync(aggregate, idempotencyId, batchOperation, cancellationToken);

			// We create a snapshot if it's been deleted or restored, could make searching easier later on.
			if (shouldSnapshot)
				await CreateSnapshotAsync(aggregate, cancellationToken);

			if (changeEvents.Any(record => record.Event is Deleted))
				_eventStoreTelemetry.AggregateDeleted(aggregate.Id(), _aggregateTypeFullName, aggregate.AggregateType);
			else if (changeEvents.Any(record => record.Event is Restored))
				_eventStoreTelemetry.AggregateRestored(aggregate.Id(), _aggregateTypeFullName, aggregate.AggregateType);

			_eventStoreTelemetry.SavedAggregate(
				aggregate.Id(),
				_aggregateTypeFullName,
				changeEvents.Length,
				aggregate.AggregateType
			);

			// Do not pass in the cancellation token. We want this to carry on as long as possible.
			await UpdateCacheAsync(aggregate, operationContext.CacheOptions, cancellationToken);

			// ...or here.
			if (aggregate.Details.IsDeleted && operationContext.NotificationMode.HasFlag(NotificationModes.AfterDelete))
				await _aggregateChangeNotifier.AfterDeleteAsync(aggregate, cancellationToken);
			else if (operationContext.NotificationMode.HasFlag(NotificationModes.AfterSave))
				await _aggregateChangeNotifier.AfterSaveAsync(
					aggregate,
					previousAggregateVersion,
					isNew,
					changeEvents,
					cancellationToken
				);
		}
		catch (Exception ex)
		{
			ClearCacheFireAndForget(aggregate);

			await HandleSaveFailureAsync(aggregate, operationContext, changeEvents, ex, cancellationToken);

			if (ex is ConcurrencyException conflict)
				return AggregateSaveFailure.Failure<SaveOutcome<T>>(ToConcurrencyConflict(aggregate, conflict));

			throw;
		}

		return new SaveOutcome<T>(aggregate, SaveOutcomeKind.Written);
	}

	static ConcurrencyConflict ToConcurrencyConflict(T aggregate, ConcurrencyException exception) =>
		new(aggregate.AggregateType, aggregate.Id(), exception.VersionAttempted, exception.Version);

	async Task HandleSaveFailureAsync(
		T aggregate,
		EventStoreOperationContext operationContext,
		EventRecord[] changeEvents,
		Exception exception,
		CancellationToken cancellationToken
	)
	{
		if (!operationContext.NotificationMode.HasFlag(NotificationModes.OnFailure))
			return;

		var deleteRequested = changeEvents.Any(record => record.Event is Deleted);
		await _aggregateChangeNotifier.FailureAsync(aggregate, deleteRequested, exception, cancellationToken);
	}

	sealed class MongoSavePreparation(
		Result<SaveOutcome<T>, AggregateSaveFailure>? terminal,
		T aggregate,
		EventStoreOperationContext operationContext,
		string idempotencyId,
		EventRecord[] changeEvents,
		bool isNew,
		IdempotencyMarkerEntity? marker
	)
	{
		public Result<SaveOutcome<T>, AggregateSaveFailure>? Terminal => terminal;
		public T Aggregate => aggregate;
		public EventStoreOperationContext OperationContext => operationContext;
		public string IdempotencyId => idempotencyId;
		public EventRecord[] ChangeEvents => changeEvents;
		public bool IsNew => isNew;
		public IdempotencyMarkerEntity? Marker => marker;
	}

	async Task<ValidationResult> GuardAsync(T aggregate, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		return _validator == null
			? await DefaultAggregateValidator<T>.Instance.ValidateAsync(aggregate, cancellationToken)
			: await _validator.ValidateAsync(aggregate, cancellationToken);
		;
	}

	bool ShouldSnapShot(T aggregate, EventRecord[] events)
	{
		// A snapshot serializes aggregate state, which holds the same personal data as the protected
		// events in clear - and one snapshot can span many subjects, so no single key can protect it.
		// With protection enabled the store does not snapshot at all: a snapshot surviving key erasure
		// would make crypto-shredding untrue. Replay from events stays correct, only slower.
		if (_payloadProtector is not null)
			return false;
		return aggregate.Details.IsDeleted || events.Any(record => record.Event is Restored) || events.Length > 0;
	}

	IdempotencyMarkerEntity CreateIdempotencyMarkerOperation(
		T aggregate,
		string idempotencyId,
		EventRecord[] changeEvents
	)
	{
		IdempotencyMarkerEntity marker = new()
		{
			Id = CreateIdempotencyCheckId(aggregate.Id(), idempotencyId),
			AggregateId = aggregate.Id(),
			EventVersions = [.. changeEvents.Select(m => m.Metadata.AggregateVersion).OrderBy(m => m)],
			Timestamp = DateTimeOffset.UtcNow,
		};

		return marker;
	}

	async Task SubmitBatchOperationsAsync(
		T aggregate,
		string idempotencyId,
		BatchOperation batchOperation,
		CancellationToken cancellationToken
	)
	{
		try
		{
			await _eventClient.SubmitBatchAsync(batchOperation, cancellationToken);

			var currentVersion = aggregate.Details.CurrentVersion;

			aggregate.ClearUnsavedEvents();

			aggregate.Details.CurrentVersion = aggregate.Details.SavedVersion = currentVersion;
			aggregate.Details.Etag = currentVersion.ToString(CultureInfo.InvariantCulture);
		}
		catch (MongoWriteException ex)
		{
			_eventStoreTelemetry.SaveFailedAtStorage(aggregate.Id(), _aggregateTypeFullName, ex);

			ClearCacheFireAndForget(aggregate);

			// A duplicate key on the event/stream rows means another writer already persisted
			// the same aggregate version. Surface this as a concurrency conflict so callers can
			// apply the standard retry path instead of treating it as an opaque commit failure.
			if (MongoDBClient.IsDuplicateKeyError(ex))
				throw new ConcurrencyException(
					aggregate.Id(),
					idempotencyId,
					aggregate.Details.CurrentVersion,
					aggregate.Details.SavedVersion
				);

			throw new CommitException(
				aggregate.Id(),
				idempotencyId,
				aggregate.Details.CurrentVersion,
				aggregate.Details.SavedVersion,
				ex
			);
		}
		catch (Exception ex)
		{
			_eventStoreTelemetry.SaveFailed(aggregate.Id(), _aggregateTypeFullName, ex);

			ClearCacheFireAndForget(aggregate);

			throw;
		}
	}

	async Task CreateSnapshotAsync(T aggregate, CancellationToken cancellationToken)
	{
		// Set the snapshot version to the current version...
		aggregate.Details.SnapshotVersion = aggregate.Details.CurrentVersion;

		var snapshot = SerializeSnapshot(aggregate);

		SnapshotEntity snapshotEntity = new()
		{
			Id = aggregate.Id(),
			AggregateType = _aggregateTypeShortName,
			AggregateFullType = _aggregateTypeFullName,
			SchemaVersion = _snapshotSchemaVersion,
			Timestamp = DateTimeOffset.UtcNow,
			Payload = snapshot,
		};

		await _snapshotClient.UpsertAsync(
			snapshotEntity,
			m => m.Id == snapshotEntity.Id && m.EntityType == EntityTypes.SnapshotType,
			cancellationToken
		);
	}

	EventEntity CreateSerializedEvent(
		string aggregateId,
		object @event,
		string serializedEvent,
		EventMetadata metadata,
		string idempotencyMarkerId
	) =>
		new()
		{
			Id = CreateEventId(aggregateId, metadata.AggregateVersion),
			AggregateId = aggregateId,
			Version = metadata.AggregateVersion,
			Payload = serializedEvent,
			EventType = _eventNameMapper.GetName<T>(@event),
			IdempotencyId = idempotencyMarkerId,
			SchemaVersion = metadata.SchemaVersion,
			CorrelationId = metadata.CorrelationId,
			CausationId = metadata.CausationId,
			UserId = metadata.UserId,
			Timestamp = DateTimeOffset.UtcNow,
		};

	void ClearCacheFireAndForget(T aggregate)
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

	/// <summary>
	/// Encrypts a serialized payload when its event names a data subject.
	/// </summary>
	/// <remarks>
	/// Applied after serialization so the stored payload is the envelope, which keeps the persistence
	/// schema unchanged. An event with no data subject, or a store with no protector configured, is
	/// stored as before.
	/// </remarks>
	Task<string> ProtectIfRequiredAsync(object changeEvent, string serialized, CancellationToken cancellationToken) =>
		_payloadProtector is not null && changeEvent is IEventDataSubject dataSubject
			? _payloadProtector.ProtectAsync(serialized, dataSubject.DataSubjectId, cancellationToken)
			: Task.FromResult(serialized);
}
