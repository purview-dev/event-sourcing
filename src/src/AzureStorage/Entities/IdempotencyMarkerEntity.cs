using Azure;
using Azure.Data.Tables;

namespace Purview.EventSourcing.AzureStorage.Entities;

/// <summary>
/// A persisted idempotency marker row in Azure Table Storage.
/// </summary>
/// <remarks>
/// The marker records which event versions were persisted for a given idempotency id, allowing the store to
/// detect and skip already-applied saves.
/// </remarks>
public sealed class IdempotencyMarkerEntity : ITableEntity, IHasTenant
{
	/// <summary>
	/// Initializes a new instance of the <see cref="IdempotencyMarkerEntity"/> class with the given keys.
	/// </summary>
	/// <param name="partitionKey">The partition key of the marker, which is the aggregate id.</param>
	/// <param name="rowKey">The row key of the marker, which encodes the idempotency id.</param>
	public IdempotencyMarkerEntity(string partitionKey, string rowKey)
	{
		PartitionKey = partitionKey;
		RowKey = rowKey;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="IdempotencyMarkerEntity"/> class.
	/// </summary>
	public IdempotencyMarkerEntity() { }

	/// <summary>
	/// Gets or sets the serialized event ids that were persisted for the idempotency id.
	/// </summary>
	public string Events { get; set; } = default!;

	/// <summary>
	/// Gets or sets the partition key of the entity, which is the aggregate id.
	/// </summary>
	public string PartitionKey { get; set; } = default!;

	/// <summary>
	/// Gets or sets the row key of the entity, which encodes the idempotency id.
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
