using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using MongoDB.Driver;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.MongoDB.Events.Entities;
using Purview.EventSourcing.MongoDB.Events.Exceptions;
using Purview.EventSourcing.MongoDB.StorageClient;
using Purview.EventSourcing.Storage;
using Purview.EventSourcing.Validation;

namespace Purview.EventSourcing.MongoDB;

partial class MongoDBEventStore<T>
{
	///<inheritdoc/>
	[DebuggerStepThrough]
	public Task<SaveResult<T>> SaveAsync(
		[NotNull] T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => SaveCoreAsync(aggregate, operationContext, cancellationToken);

	async Task<SaveResult<T>> SaveCoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken,
		params EventRecord[] additionalEvents
	)
	{
		var preparation = await PrepareSaveAsync(aggregate, operationContext, additionalEvents, cancellationToken);

		return preparation.Terminal ?? await PersistAndNotifyAsync(preparation, cancellationToken);
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
				SaveResultBuilder.Create(aggregate, false, false, validationResult),
				aggregate,
				operationContext,
				idempotencyId,
				[],
				isNew: false,
				marker: null
			);

		if (aggregate.Details.Locked)
		{
			return operationContext.LockMode is LockHandlingMode.ThrowsException
				? throw new AggregateLockedException(idempotencyId)
				: new MongoSavePreparation(
					SaveResultBuilder.Create(aggregate, false, false),
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
				SaveResultBuilder.Create(aggregate, false, true),
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
					SaveResultBuilder.Create(aggregate, true, true),
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

	async Task<SaveResult<T>> PersistAndNotifyAsync(
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
		if (streamEntity?.IsDeleted == true)
		{
			var throwIfDeleted = !changeEvents.Any(record => record.Event is Restored);
			if (throwIfDeleted)
				throw new AggregateDeletedException(aggregate.Id(), idempotencyId);
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

			var userId = ClaimsPrincipal.Current?.FindFirst(operationContext.ClaimIdentifier)?.Value;
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

				var serializedEvent = SerializeEvent(changeEvent);
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

			if (operationContext.NotificationMode.HasFlag(NotificationModes.OnFailure))
			{
				var deleteRequested = changeEvents.Any(record => record.Event is Deleted);
				await _aggregateChangeNotifier.FailureAsync(aggregate, deleteRequested, ex, cancellationToken);
			}

			throw;
		}

		return SaveResultBuilder.Create(aggregate, true, false);
	}

	sealed class MongoSavePreparation(
		SaveResult<T>? terminal,
		T aggregate,
		EventStoreOperationContext operationContext,
		string idempotencyId,
		EventRecord[] changeEvents,
		bool isNew,
		IdempotencyMarkerEntity? marker
	)
	{
		public SaveResult<T>? Terminal => terminal;
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

	static bool ShouldSnapShot(T aggregate, EventRecord[] events)
	{
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
}
