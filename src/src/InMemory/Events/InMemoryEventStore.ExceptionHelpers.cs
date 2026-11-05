using Purview.EventSourcing.Exceptions;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.InMemory.Events;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter")]
partial class InMemoryEventStore<T>
{
	static readonly string AggregateTypeName = new T().AggregateType;

	static ArgumentNullException NullAggregate(T? aggregate) => new(nameof(aggregate));

	static MissingAggregateIdException MissingAggregateIdException() => new(string.Empty);

	Result<T, AggregateReadFailure> NotFound(string aggregateId) =>
		AggregateReadFailure.Failure<T>(new AggregateNotFound(AggregateTypeName, aggregateId));

	Result<T, AggregateReadFailure> Deleted(string aggregateId) =>
		AggregateReadFailure.Failure<T>(new AggregateDeleted(AggregateTypeName, aggregateId));

	Result<T, AggregateReadFailure> NotDeleted(string aggregateId) =>
		AggregateReadFailure.Failure<T>(new AggregateNotDeleted(AggregateTypeName, aggregateId));
}
