using System.Diagnostics;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Purview.EventSourcing.MongoDB.StorageClient;

namespace Purview.EventSourcing.MongoDB.Events.Entities;

/// <summary>
/// The persisted representation of a single event in the MongoDB event collection.
/// </summary>
/// <remarks>
/// The <see cref="Payload"/> holds the serialized event content and <see cref="EventType"/> the
/// serialized event name used to resolve the runtime event type when replaying the stream.
/// </remarks>
[DebuggerStepThrough]
public sealed class EventEntity : IEntity
{
	/// <summary>
	/// The unique identifier of the event record.
	/// </summary>
	[BsonId]
	[JsonPropertyName("id")]
	public string Id { get; set; } = default!;

	/// <summary>
	/// The discriminator identifying the entity as an event.
	/// </summary>
	public int EntityType { get; set; } = EntityTypes.EventType;

	/// <summary>
	/// The tenant that owns the document; null in a single-tenant deployment.
	/// </summary>
	public string? TenantId { get; set; }

	/// <summary>
	/// The identifier of the aggregate the event belongs to.
	/// </summary>
	public string AggregateId { get; set; } = default!;

	/// <summary>
	/// The aggregate version the event was applied at.
	/// </summary>
	public long Version { get; set; }

	/// <summary>
	/// The serialized event payload.
	/// </summary>
	public string Payload { get; set; } = default!;

	/// <summary>
	/// The serialized name of the event type.
	/// </summary>
	public string EventType { get; set; } = default!;

	/// <summary>
	/// The idempotency identifier of the save operation that produced the event.
	/// </summary>
	public string IdempotencyId { get; set; } = default!;

	/// <summary>The event contract schema version.</summary>
	public int SchemaVersion { get; set; } = 1;

	/// <summary>The correlation identifier recorded with the event.</summary>
	public string? CorrelationId { get; set; }

	/// <summary>The causation identifier recorded with the event.</summary>
	public string? CausationId { get; set; }

	/// <summary>The user identifier recorded with the event.</summary>
	public string? UserId { get; set; }

	/// <summary>
	/// The time the event was persisted.
	/// </summary>
	public DateTimeOffset? Timestamp { get; set; }
}
