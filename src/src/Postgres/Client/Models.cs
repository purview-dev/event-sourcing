namespace Purview.EventSourcing.Postgres.Client;

sealed record SnapshotStorageRow(string Id, string AggregateType, string Payload, string? TenantId = null);

sealed class SnapshotQueryRow<TAggregate>
	where TAggregate : class
{
	public required string Id { get; set; }

	public required string AggregateType { get; set; }

	public required TAggregate Payload { get; set; }

	/// <summary>
	/// The tenant that owns the snapshot; null in a single-tenant deployment.
	/// </summary>
	public string? TenantId { get; set; }
}

sealed record PostgresClientOptions(string ConnectionString, bool UseDataCompression)
{
	public string TableName { get; init; } = "EventStoreSnapshots";

	public string SchemaName { get; init; } = "public";

	public bool AutoCreateTable { get; init; } = true;

	public PostgresJsonIndexOptions JsonIndexOptions { get; init; } = new();
}
