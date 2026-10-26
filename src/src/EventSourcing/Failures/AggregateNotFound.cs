namespace Purview.EventSourcing.Failures;

/// <summary>
/// No stream exists for the requested aggregate.
/// </summary>
/// <param name="AggregateType">The aggregate type name.</param>
/// <param name="AggregateId">The identifier that was requested.</param>
public readonly record struct AggregateNotFound(string AggregateType, string AggregateId);
