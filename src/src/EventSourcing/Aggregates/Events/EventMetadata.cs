namespace Purview.EventSourcing.Aggregates.Events;

/// <summary>
/// Framework-managed metadata for an event, carried alongside the event payload rather than inside
/// it. The struct is immutable and allocation-free.
/// </summary>
public readonly record struct EventMetadata(
	int AggregateVersion,
	DateTimeOffset When,
	int SchemaVersion,
	string? IdempotencyId,
	string? CorrelationId,
	string? CausationId,
	string? UserId
);
