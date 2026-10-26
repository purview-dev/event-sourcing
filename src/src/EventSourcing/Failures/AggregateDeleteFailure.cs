using Purview.Results;

namespace Purview.EventSourcing.Failures;

/// <summary>
/// The expected ways a delete of an aggregate fails.
/// </summary>
/// <remarks>
/// Every case is shared with another failure union, so construct them through
/// <c>AggregateDeleteFailure.Failure(...)</c> rather than the per-case helper.
/// </remarks>
[GenerateResult]
public readonly union AggregateDeleteFailure(
    AggregateNotFound,
    AggregateAlreadyDeleted,
    AggregateLocked,
    ConcurrencyConflict,
    AggregateInvalid
);
