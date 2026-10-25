using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.MongoDB.Events.Entities;
using Purview.Results;

namespace Purview.EventSourcing.MongoDB;

partial class MongoDBEventStore<T>
{
	///<inheritdoc/>
	public async Task<Result<SaveOutcome<T>, AggregateDeleteFailure>> DeleteAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		if (aggregate == null)
			throw NullAggregate(aggregate);

		if (aggregate.Details.IsDeleted)
		{
			return AggregateDeleteFailure.Failure<SaveOutcome<T>>(
				new AggregateAlreadyDeleted(aggregate.AggregateType, aggregate.Id())
			);
		}

		operationContext ??= EventStoreOperationContext.DefaultContext();

		if (aggregate.IsNew())
		{
			return AggregateDeleteFailure.Failure<SaveOutcome<T>>(
				new AggregateNotFound(aggregate.AggregateType, aggregate.Id())
			);
		}

		if (operationContext.PermanentlyDelete)
			return await PermanentlyDeleteAsync(aggregate, operationContext, cancellationToken);

		Deleted deleteAggregateEvent = new();
		EventMetadata metadata = new(
			aggregate.Details.CurrentVersion + 1,
			DateTimeOffset.UtcNow,
			SchemaVersion: 1,
			IdempotencyId: null,
			CorrelationId: null,
			CausationId: null,
			UserId: null
		);
		aggregate.ApplyEvent(deleteAggregateEvent, metadata);

		var result = await SaveCoreAsync(
			aggregate,
			operationContext,
			cancellationToken,
			new EventRecord(deleteAggregateEvent, metadata)
		);

		return result.IsFailure ? result.Error.ToDeleteFailure<T>() : result.Value;
	}

	async Task<Result<SaveOutcome<T>, AggregateDeleteFailure>> PermanentlyDeleteAsync(
		T aggregate,
		EventStoreOperationContext operationContext,
		CancellationToken cancellationToken = default
	)
	{
		if (aggregate == null)
			throw NullAggregate(aggregate);

		var aggregateId = aggregate.Id();
		var streamVersion = await GetStreamVersionAsync(aggregateId, true, cancellationToken);
		if (streamVersion == null)
		{
			return AggregateDeleteFailure.Failure<SaveOutcome<T>>(
				new AggregateNotFound(aggregate.AggregateType, aggregateId)
			);
		}

		_eventStoreTelemetry.PermanentDeleteRequested(aggregateId);
		try
		{
			// The stream version is a long, so the id range is built over longs: Enumerable.Range is int-only.
			List<string> ids = [];
			for (var version = 1L; version <= streamVersion.Version; version++)
				ids.Add(CreateEventId(aggregateId, version));

			List<string> entitiesToDelete = [.. ids];
			entitiesToDelete.Add(streamVersion.Id);

			if (operationContext.UseIdempotencyMarker)
			{
				var results = _eventClient.GetQueryEnumerableAsync<IdempotencyMarkerEntity>(
					m => m.AggregateId == aggregateId && m.EntityType == EntityTypes.IdempotencyMarkerType,
					cancellationToken: cancellationToken
				);
				await foreach (var entity in results)
					entitiesToDelete.Add(entity.Id);
			}

			await _eventClient.SubmitDeleteBatchAsync(entitiesToDelete, cancellationToken);
			await _snapshotClient.DeleteAsync<SnapshotEntity>(m => m.Id == aggregateId, cancellationToken);

			_eventStoreTelemetry.PermanentDeleteComplete(aggregateId);

			aggregate.Details.IsDeleted = true;
			aggregate.Details.Locked = true;

			return new SaveOutcome<T>(aggregate, SaveOutcomeKind.Written);
		}
#pragma warning disable CA1031
		catch (Exception ex)
#pragma warning restore CA1031
		{
			_eventStoreTelemetry.PermanentDeleteFailed(aggregateId, ex);

			throw;
		}
		finally
		{
			ClearCacheFireAndForget(aggregate);
		}
	}
}
