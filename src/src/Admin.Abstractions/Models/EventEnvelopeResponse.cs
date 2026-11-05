using System.Text.Json;

namespace Purview.EventSourcing.Admin.Models;

/// <summary>
/// Represents a single event together with its metadata and raw JSON payload.
/// </summary>
/// <param name="AggregateType">The aggregate type name.</param>
/// <param name="AggregateId">The aggregate identifier.</param>
/// <param name="Metadata">The event metadata.</param>
/// <param name="Payload">The event payload as raw JSON, or <see langword="null"/> when payload access is not granted.</param>
public sealed record EventEnvelopeResponse(
	string AggregateType,
	string AggregateId,
	EventMetadataResponse Metadata,
	JsonElement? Payload
);
