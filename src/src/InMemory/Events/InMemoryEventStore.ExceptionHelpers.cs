namespace Purview.EventSourcing.InMemory.Events;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter")]
partial class InMemoryEventStore<T>
{
	static ArgumentNullException NullAggregate(T? aggregate) => new(nameof(aggregate));

	static Exception AggregateIsDeletedException(string aggregateId) =>
		new($"Aggregate with ID '{aggregateId}' is deleted.");

	static Exception AggregateNotDeletedException(string aggregateId) =>
		new($"Aggregate with ID '{aggregateId}' is not deleted.");

	static Exception MissingAggregateIdException() => new("Aggregate ID is missing.");

	static Exception AggregateLockedException(string aggregateId) =>
		new($"Aggregate with ID '{aggregateId}' is locked.");
}
