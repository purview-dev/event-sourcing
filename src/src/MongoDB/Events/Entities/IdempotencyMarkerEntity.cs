using System.Diagnostics;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Purview.EventSourcing.MongoDB.StorageClient;

namespace Purview.EventSourcing.MongoDB.Events.Entities;

/// <summary>
/// The persisted representation of an idempotency marker used to detect duplicate save operations.
/// </summary>
/// <remarks>
/// When idempotency markers are enabled, a marker is created for each save operation and records the
/// aggregate versions of the events written by that operation, allowing the store to detect and ignore
/// replayed or duplicate saves.
/// </remarks>
[DebuggerStepThrough]
public sealed class IdempotencyMarkerEntity : IEntity
{
	/// <summary>
	/// The unique identifier of the idempotency marker.
	/// </summary>
	[BsonId]
	[JsonPropertyName("id")]
	public string Id { get; set; } = default!;

	/// <summary>
	/// The discriminator identifying the entity as an idempotency marker.
	/// </summary>
	public int EntityType { get; set; } = EntityTypes.IdempotencyMarkerType;

	/// <summary>
	/// The tenant that owns the document; null in a single-tenant deployment.
	/// </summary>
	public string? TenantId { get; set; }

	/// <summary>
	/// The identifier of the aggregate the save operation targeted.
	/// </summary>
	public string AggregateId { get; set; } = default!;

	/// <summary>
	/// The aggregate versions of the events written by the save operation.
	/// </summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Performance",
		"CA1819:Properties should not return arrays",
		Justification = "This is a DTO."
	)]
	public long[] EventVersions { get; set; } = [];

	/// <summary>
	/// The time the marker was persisted.
	/// </summary>
	public DateTimeOffset? Timestamp { get; set; }
}
