using System.ComponentModel.DataAnnotations;
using Microsoft.Azure.Cosmos;

namespace Purview.EventSourcing.CosmosDb;

/// <summary>
/// Configuration options for the Azure Cosmos DB snapshot event store.
/// </summary>
/// <remarks>
/// The options are bound from the <c>"EventStore:CosmosDbSnapshot"</c> configuration section
/// (see <see cref="Snapshots.CosmosDbEventStoreOptions.CosmosDbEventStore"/>) and validated on startup.
/// </remarks>
public class CosmosDbOptions
{
	/// <summary>
	/// Gets or sets the default request timeout, in seconds, used when no
	/// <see cref="RequestTimeoutInSeconds"/> value is supplied.
	/// </summary>
	public static int DefaultRequestTimeout { get; set; } = 5;

	/// <summary>
	/// Gets or sets the default throughput (RU/s) used when creating a new database.
	/// </summary>
	public static int DefaultDatabaseThroughput { get; set; } = MinimumThroughput;

	/// <summary>
	/// Gets or sets the default throughput (RU/s) used when creating a new container.
	/// </summary>
	public static int DefaultContainerThroughput { get; set; } = MinimumThroughput;

	const int MinimumThroughput = 400;

	/// <summary>
	/// Gets or sets the connection mode used by the Azure Cosmos DB client.
	/// </summary>
	[Required]
	public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.Direct;

	/// <summary>
	/// Gets or sets the connection string used to connect to the Azure Cosmos DB account.
	/// </summary>
	[Required]
	public string ConnectionString { get; set; } = default!;

	/// <summary>
	/// Gets or sets the name of the database to use.
	/// </summary>
	[Required]
	[RegularExpression(@"^[\w\-.]+$")]
	public string Database { get; set; } = default!;

	/// <summary>
	/// Gets or sets the name of the container to use.
	/// </summary>
	[Required]
	[RegularExpression(@"^[\w\-.]+$")]
	public string Container { get; set; } = default!;

	/// <summary>
	/// Gets or sets the request timeout, in seconds; when null, <see cref="DefaultRequestTimeout"/> is used.
	/// </summary>
	[Range(1, 120000)]
	public int? RequestTimeoutInSeconds { get; set; } = DefaultRequestTimeout;

	/// <summary>
	/// This is only used when creating a non-existent database, it does not modify existing databases.
	/// </summary>
	[Range(MinimumThroughput, int.MaxValue)]
	public int DatabaseThroughput { get; set; } = DefaultDatabaseThroughput;

	/// <summary>
	/// This is only used when creating a non-existent collection, it does not modify existing collections.
	/// </summary>
	[Range(MinimumThroughput, int.MaxValue)]
	public int ContainerThroughput { get; set; } = DefaultContainerThroughput;

	/// <summary>
	/// Gets or sets the partition key path used when creating the container.
	/// </summary>
	/// <remarks>
	/// Snapshot documents are partitioned by aggregate type, so this must remain
	/// <c>"/AggregateType"</c>; any other value is rejected when the store is created.
	/// </remarks>
	[Required]
	[RegularExpression("^[/].+$")]
	public string PartitionKeyPath { get; set; } = default!;

	/// <summary>
	/// WARNING: This is used when connecting to emulators only, i.e. for testing purposes.
	/// </summary>
	public bool IgnoreSSLWarnings { get; set; }

	/// <summary>
	/// Gets or sets the indexing policy options applied when creating or updating the container.
	/// </summary>
	[Required]
	public CosmosDbIndexOptions IndexOptions { get; set; } = new();
}
