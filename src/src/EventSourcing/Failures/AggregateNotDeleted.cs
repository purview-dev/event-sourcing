namespace Purview.EventSourcing.Failures;

/// <summary>
/// A restore was attempted on an aggregate that is not deleted.
/// </summary>
/// <param name="AggregateType">The aggregate type name.</param>
/// <param name="AggregateId">The identifier of the aggregate.</param>
public readonly record struct AggregateNotDeleted(string AggregateType, string AggregateId);
