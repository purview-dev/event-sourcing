using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.Postgres.Events;

partial class PostgresEventStore<T>
{
	///<inheritdoc/>
	public async Task<bool> DeleteAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		if (aggregate == null)
			throw NullAggregate(aggregate);

		if (aggregate.Details.IsDeleted)
			throw AggregateIsDeletedException(aggregate.Id());

		operationContext ??= EventStoreOperationContext.DefaultContext();

		if (aggregate.IsNew())
			return false;

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
		await result.AfterCommitAsync(cancellationToken);

		return result.Result.Saved;
	}

	async Task<bool> PermanentlyDeleteAsync(
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
			return false;

		_eventStoreTelemetry.PermanentDeleteRequested(aggregateId);
		using var activity = _eventStoreTelemetry.DeleteAggregate(aggregateId, _aggregateTypeFullName);
		try
		{
			await _client.DeleteByAggregateIdAsync(aggregateId, _aggregateTypeShortName, cancellationToken);

			_eventStoreTelemetry.PermanentDeleteComplete(aggregateId);
			_eventStoreTelemetry.AggregateDeletedCounter(aggregate.AggregateType);

			aggregate.Details.IsDeleted = true;
			aggregate.Details.Locked = true;

			return true;
		}
#pragma warning disable CA1031
		catch (Exception ex)
#pragma warning restore CA1031
		{
			_eventStoreTelemetry.PermanentDeleteFailed(aggregateId, ex);

			return false;
		}
		finally
		{
			ClearCacheFireAndForget(aggregate);
		}
	}
}
