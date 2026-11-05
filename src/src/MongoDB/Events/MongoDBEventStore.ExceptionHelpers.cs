using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.MongoDB;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter")]
partial class MongoDBEventStore<T>
{
	static ArgumentNullException NullAggregate(T? aggregate) => new(nameof(aggregate));

	Result<T, AggregateReadFailure> NotFound(string aggregateId) =>
		AggregateReadFailure.Failure<T>(new AggregateNotFound(_aggregateTypeShortName, aggregateId));

	Result<T, AggregateReadFailure> Deleted(string aggregateId) =>
		AggregateReadFailure.Failure<T>(new AggregateDeleted(_aggregateTypeShortName, aggregateId));

	Result<T, AggregateReadFailure> NotDeleted(string aggregateId) =>
		AggregateReadFailure.Failure<T>(new AggregateNotDeleted(_aggregateTypeShortName, aggregateId));
}
