using Azure;
using Azure.Data.Tables;

namespace Purview.EventSourcing.AzureStorage.Entities;

/// <summary>
/// A persisted stream-version row in Azure Table Storage.
/// </summary>
/// <remarks>
/// Each aggregate has a single stream-version entity that records its most recently saved version, whether it
/// has been deleted, and the aggregate type. It is used to check existence and enforce optimistic concurrency.
/// </remarks>
public sealed class StreamVersionEntity : ITableEntity, IHasTenant
{
	/// <summary>
	/// Gets or sets a value indicating whether the aggregate has been deleted.
	/// </summary>
	public bool IsDeleted { get; set; }

	/// <summary>
	/// Gets or sets the name of the aggregate type.
	/// </summary>
	public string AggregateType { get; set; } = default!;

	/// <summary>
	/// This is the most recently saved version of the aggregate.
	/// </summary>
	public long Version { get; set; }

	/// <summary>
	/// Gets or sets the partition key of the entity, which is the aggregate id.
	/// </summary>
	public string PartitionKey { get; set; } = default!;

	/// <summary>
	/// Gets or sets the row key of the entity.
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
