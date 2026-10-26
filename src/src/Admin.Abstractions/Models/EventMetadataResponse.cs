namespace Purview.EventSourcing.Admin.Models;

/// <summary>
/// Describes the metadata associated with a single event in an aggregate stream.
/// </summary>
/// <param name="Version">The stream version of the event.</param>
/// <param name="TimestampUtc">The UTC timestamp at which the event was recorded.</param>
/// <param name="EventType">The event type name.</param>
/// <param name="SchemaVersion">The schema version of the event payload.</param>
/// <param name="CorrelationId">The correlation identifier, when one was recorded.</param>
/// <param name="CausationId">The causation identifier, when one was recorded.</param>
/// <param name="IdempotencyId">The idempotency identifier, when one was recorded.</param>
/// <param name="UserId">The identifier of the user that caused the event, when known.</param>
public sealed record EventMetadataResponse(
	long Version,
	DateTimeOffset TimestampUtc,
	string EventType,
	int SchemaVersion,
	string? CorrelationId,
	string? CausationId,
	string? IdempotencyId,
	string? UserId
);
