using Azure;
using Azure.Data.Tables;

namespace Purview.EventSourcing.AzureStorage.Entities;

/// <summary>
/// A persisted event row in Azure Table Storage.
/// </summary>
/// <remarks>
/// Each row stores the serialized event payload, the resolved event type name, and the idempotency id that
/// produced the event. The <see cref="PartitionKey"/> is the aggregate id and the <see cref="RowKey"/>
/// encodes the aggregate version.
/// </remarks>
public sealed class EventEntity : ITableEntity, IHasTenant
{
	/// <summary>
	/// Gets or sets the serialized event payload.
	/// </summary>
	public string Payload { get; set; } = default!;

	/// <summary>
	/// Gets or sets the name of the event type.
	/// </summary>
	public string EventType { get; set; } = default!;

	/// <summary>
	/// Gets or sets the idempotency id associated with the event.
	/// </summary>
	public string IdempotencyId { get; set; } = default!;

	/// <summary>Gets or sets the event contract schema version.</summary>
	public int SchemaVersion { get; set; } = 1;

	/// <summary>Gets or sets the correlation identifier.</summary>
	public string? CorrelationId { get; set; }

	/// <summary>Gets or sets the causation identifier.</summary>
	public string? CausationId { get; set; }

	/// <summary>Gets or sets the user identifier.</summary>
	public string? UserId { get; set; }

	/// <summary>
	/// Gets or sets the partition key of the entity, which is the aggregate id.
	/// </summary>
	public string PartitionKey { get; set; } = default!;

	/// <summary>
	/// Gets or sets the row key of the entity, which encodes the aggregate version.
	/// </summary>
	public string RowKey { get; set; } = default!;

	/// <summary>
	/// Gets or sets the timestamp of the entity.
	/// </summary>
	public DateTimeOffset? Timestamp { get; set; }

	/// <summary>
	/// Gets or sets the entity tag used for optimistic concurrency.
	/// </summary>
	/// <summary>
	/// The tenant that owns the row; null in a single-tenant deployment.
	/// </summary>
	public string? TenantId { get; set; }

	public ETag ETag { get; set; }
}
