using Purview.Results;

namespace Purview.EventSourcing.Failures;

/// <summary>
/// The expected ways a read of an aggregate fails.
/// </summary>
/// <remarks>
/// <see cref="AggregateNotDeleted"/> belongs to the family because
/// <c>GetDeletedAsync</c> is a read: it reports a live aggregate rather than returning one the
/// caller asked for in its deleted state. The other reads never produce it.
/// </remarks>
[GenerateResult]
public readonly union AggregateReadFailure(
	AggregateNotFound,
	AggregateDeleted,
	AggregateNotDeleted,
	AggregateLocked
);
