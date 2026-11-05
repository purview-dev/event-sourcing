using System.Diagnostics;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Purview.EventSourcing.MongoDB.StorageClient;

namespace Purview.EventSourcing.MongoDB.Events.Entities;

/// <summary>
/// The persisted representation of an aggregate snapshot in the MongoDB snapshot collection.
/// </summary>
/// <remarks>
/// The <see cref="Payload"/> holds the serialized aggregate state at the time the snapshot was taken.
/// Snapshots are an optimization and never the source of truth; the event stream remains canonical.
/// </remarks>
[DebuggerStepThrough]
public sealed class SnapshotEntity : IEntity
{
	/// <summary>
	/// The unique identifier of the snapshot record, which is also the identifier of the aggregate.
	/// </summary>
	[BsonId]
	[JsonPropertyName("id")]
	public string Id { get; set; } = default!;

	/// <summary>
	/// Gets or sets the identifier of the aggregate the snapshot belongs to.
	/// </summary>
	/// <remarks>
	/// Backed by <see cref="Id"/> and excluded from BSON serialization so the aggregate id remains the
	/// document identifier.
	/// </remarks>
	[BsonIgnore]
	public string AggregateId
	{
		get => Id;
		set => Id = value;
	}

	/// <summary>
	/// The discriminator identifying the entity as a snapshot.
	/// </summary>
	public int EntityType { get; set; } = EntityTypes.SnapshotType;

	/// <summary>
	/// The tenant that owns the document; null in a single-tenant deployment.
	/// </summary>
	public string? TenantId { get; set; }

	/// <summary>
	/// The short name of the aggregate type the snapshot belongs to.
	/// </summary>
	public string AggregateType { get; set; } = default!;

	/// <summary>
	/// The full name of the aggregate type the snapshot belongs to.
	/// </summary>
	public string AggregateFullType { get; set; } = default!;

	/// <summary>The aggregate snapshot serialization schema version.</summary>
	public int SchemaVersion { get; set; } = 1;

	/// <summary>
	/// The time the snapshot was persisted.
	/// </summary>
	public DateTimeOffset? Timestamp { get; set; }

	/// <summary>
	/// The serialized aggregate state captured by the snapshot.
	/// </summary>
	public string Payload { get; set; } = default!;
}
