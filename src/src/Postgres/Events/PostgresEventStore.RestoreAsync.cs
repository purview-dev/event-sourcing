using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.Postgres.Events;

partial class PostgresEventStore<T>
{
	///<inheritdoc/>
	public async Task<Result<SaveOutcome<T>, AggregateRestoreFailure>> RestoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		if (aggregate == null)
			throw NullAggregate(aggregate);

		if (!aggregate.Details.IsDeleted)
		{
			return AggregateRestoreFailure.Failure<SaveOutcome<T>>(
				new AggregateNotDeleted(aggregate.AggregateType, aggregate.Id())
			);
		}

		operationContext ??= EventStoreOperationContext.DefaultContext();

		Restored restoreAggregateEvent = new();
		EventMetadata metadata = new(
			aggregate.Details.CurrentVersion + 1,
			DateTimeOffset.UtcNow,
			SchemaVersion: 1,
			IdempotencyId: null,
			CorrelationId: null,
			CausationId: null,
			UserId: null
		);
		aggregate.ApplyEvent(restoreAggregateEvent, metadata);

		if (aggregate.IsNew())
		{
			return AggregateRestoreFailure.Failure<SaveOutcome<T>>(
				new AggregateNotFound(aggregate.AggregateType, aggregate.Id())
			);
		}

		var result = await SaveCoreAsync(
			aggregate,
			operationContext,
			null,
			null,
			cancellationToken,
			new EventRecord(restoreAggregateEvent, metadata)
		);
		if (result.Result.IsFailure)
			return result.Result.Error.ToRestoreFailure<T>();

		await result.AfterCommitAsync(cancellationToken);

		return result.Result.Value;
	}
}
