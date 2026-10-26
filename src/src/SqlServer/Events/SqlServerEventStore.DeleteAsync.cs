using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.SqlServer.Events;

partial class SqlServerEventStore<T>
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
			null,
			null,
			cancellationToken,
			new EventRecord(deleteAggregateEvent, metadata)
		);
		if (result.Result.IsFailure)
			return result.Result.Error.ToDeleteFailure<T>();

		await result.AfterCommitAsync(cancellationToken);

		return result.Result.Value;
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
		using var activity = _eventStoreTelemetry.DeleteAggregate(aggregateId, _aggregateTypeFullName);
		try
		{
			await _client.DeleteByAggregateIdAsync(aggregateId, _aggregateTypeShortName, cancellationToken);

			_eventStoreTelemetry.PermanentDeleteComplete(aggregateId);
			_eventStoreTelemetry.AggregateDeletedCounter(aggregate.AggregateType);

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
