using System.Diagnostics;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Purview.EventSourcing.MongoDB.StorageClient;

namespace Purview.EventSourcing.MongoDB.Events.Entities;

/// <summary>
/// The persisted representation of an aggregate's stream version record.
/// </summary>
/// <remarks>
/// A single record is maintained per aggregate and tracks the aggregate's saved version and soft-delete
/// state, enabling optimistic concurrency checks when saving events.
/// </remarks>
[DebuggerStepThrough]
public sealed class StreamVersionEntity : IEntity
{
	/// <summary>
	/// The unique identifier of the stream version record.
	/// </summary>
	[BsonId]
	[JsonPropertyName("id")]
	public string Id { get; set; } = default!;

	/// <summary>
	/// The identifier of the aggregate the record belongs to.
	/// </summary>
	public string AggregateId { get; set; } = default!;

	/// <summary>
	/// The discriminator identifying the entity as a stream version record.
	/// </summary>
	public int EntityType { get; set; } = EntityTypes.StreamVersionType;

	/// <summary>
	/// The tenant that owns the document; null in a single-tenant deployment.
	/// </summary>
	public string? TenantId { get; set; }

	/// <summary>
	/// Indicates whether the aggregate is soft-deleted.
	/// </summary>
	public bool IsDeleted { get; set; }

	/// <summary>
	/// The short name of the aggregate type the record belongs to.
	/// </summary>
	public string AggregateType { get; set; } = default!;

	/// <summary>
	/// This is the most recently saved version of the aggregate.
	/// </summary>
	public long Version { get; set; }

	/// <summary>
	/// The time the record was last updated.
	/// </summary>
	public DateTimeOffset? Timestamp { get; set; }
}
