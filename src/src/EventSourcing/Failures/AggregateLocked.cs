namespace Purview.EventSourcing.Failures;

/// <summary>
/// The aggregate is locked against modification.
/// </summary>
/// <param name="AggregateType">The aggregate type name.</param>
/// <param name="AggregateId">The identifier of the locked aggregate.</param>
public readonly record struct AggregateLocked(string AggregateType, string AggregateId);
