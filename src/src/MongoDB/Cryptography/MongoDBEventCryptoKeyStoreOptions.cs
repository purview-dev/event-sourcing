using System.ComponentModel.DataAnnotations;

namespace Purview.EventSourcing.MongoDB.Cryptography;

/// <summary>
/// Options that configure the MongoDB crypto key store.
/// </summary>
/// <remarks>
/// Bound from the <c>EventStore:MongoDB:Cryptography</c> configuration section. When
/// <see cref="ConnectionString"/> or <see cref="Database"/> are not supplied, the event-store values
/// are used.
/// </remarks>
public sealed class MongoDBEventCryptoKeyStoreOptions
{
	/// <summary>The configuration section name used to bind these options.</summary>
	public const string MongoDBCryptography = "EventStore:MongoDB:Cryptography";

	/// <summary>The default collection name used to persist per-subject keys.</summary>
	public const string DefaultCollectionName = "EventStoreCryptoKeys";

	/// <summary>
	/// The MongoDB connection string. When null, the event-store connection string is used.
	/// </summary>
	public string? ConnectionString { get; set; }

	/// <summary>
	/// The database that holds the checkpoint collection. When null, the event-store database is used.
	/// </summary>
	[RegularExpression(@"^[\w\-.]+$")]
	public string? Database { get; set; }

	/// <summary>The name of the collection that stores per-subject keys.</summary>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression(@"^[\w\-.]+$")]
	public string CollectionName { get; set; } = DefaultCollectionName;
}
