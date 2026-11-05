using Purview.Results;

namespace Purview.EventSourcing.Failures;

/// <summary>
/// The expected ways a restore of a deleted aggregate fails.
/// </summary>
/// <remarks>
/// Every case is shared with another failure union, so construct them through
/// <c>AggregateRestoreFailure.Failure(...)</c> rather than the per-case helper.
/// </remarks>
[GenerateResult]
public readonly union AggregateRestoreFailure(
    AggregateNotFound,
    AggregateNotDeleted,
    AggregateLocked,
    ConcurrencyConflict,
    AggregateInvalid
);
