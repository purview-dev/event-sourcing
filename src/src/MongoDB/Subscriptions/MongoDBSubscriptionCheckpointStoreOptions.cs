using System.ComponentModel.DataAnnotations;

namespace Purview.EventSourcing.MongoDB.Subscriptions;

/// <summary>
/// Options that configure the MongoDB subscription checkpoint store.
/// </summary>
/// <remarks>
/// Bound from the <c>EventStore:MongoDB:Subscriptions</c> configuration section. When
/// <see cref="ConnectionString"/> or <see cref="Database"/> are not supplied, the event-store values
/// are used.
/// </remarks>
public sealed class MongoDBSubscriptionCheckpointStoreOptions
{
	/// <summary>The configuration section name used to bind these options.</summary>
	public const string MongoDBSubscriptions = "EventStore:MongoDB:Subscriptions";

	/// <summary>The default collection name used to persist subscription positions.</summary>
	public const string DefaultCollectionName = "EventStoreSubscriptionCheckpoints";

	/// <summary>
	/// The MongoDB connection string. When null, the event-store connection string is used.
	/// </summary>
	public string? ConnectionString { get; set; }

	/// <summary>
	/// The database that holds the checkpoint collection. When null, the event-store database is used.
	/// </summary>
	[RegularExpression(@"^[\w\-.]+$")]
	public string? Database { get; set; }

	/// <summary>The name of the collection that stores subscription positions.</summary>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression(@"^[\w\-.]+$")]
	public string CollectionName { get; set; } = DefaultCollectionName;
}
