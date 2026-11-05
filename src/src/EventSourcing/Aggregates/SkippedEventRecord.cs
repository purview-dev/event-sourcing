namespace Purview.EventSourcing.Aggregates;

/// <summary>
/// Describes an event that was skipped during replay because it could not be resolved or
/// applied by the aggregate reading the stream.
/// </summary>
/// <param name="AggregateVersion">The aggregate version the skipped event was recorded at.</param>
/// <param name="EventTypeName">The persisted event type name, when known.</param>
/// <param name="IsUnknown">True when the event type could not be resolved to a CLR type.</param>
public sealed record SkippedEventRecord(long AggregateVersion, string? EventTypeName, bool IsUnknown);
