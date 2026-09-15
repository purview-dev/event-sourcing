using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.AzureStorage;

partial class TableEventStore<T>
{
	///<inheritdoc/>
	public async Task<bool> RestoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		if (aggregate == null)
			throw NullAggregate(aggregate);

		if (!aggregate.Details.IsDeleted)
			throw AggregateNotDeletedException(aggregate.Id());

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
			return false;

		var result = await _saveOperation.SaveCoreAsync(
			aggregate,
			operationContext,
			cancellationToken,
			new EventRecord(restoreAggregateEvent, metadata)
		);
		return result.Saved;
	}
}
