using Purview.Results;

namespace Purview.EventSourcing.Failures;

/// <summary>
/// The expected ways a save of an aggregate fails.
/// </summary>
/// <remarks>
/// <see cref="AggregateDeleted"/> and <see cref="AggregateLocked"/> are shared with other failure unions, so
/// construct them through <c>AggregateSaveFailure.Failure(...)</c> rather than the per-case helper.
/// </remarks>
[GenerateResult]
public readonly union AggregateSaveFailure(
    ConcurrencyConflict,
    AggregateDeleted,
    AggregateLocked,
    AggregateInvalid
);
