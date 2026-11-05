namespace Purview.EventSourcing.Failures;

/// <summary>
/// A delete was attempted on an aggregate that is already deleted.
/// </summary>
/// <param name="AggregateType">The aggregate type name.</param>
/// <param name="AggregateId">The identifier of the aggregate.</param>
public readonly record struct AggregateAlreadyDeleted(string AggregateType, string AggregateId);
