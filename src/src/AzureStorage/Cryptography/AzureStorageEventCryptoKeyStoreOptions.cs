using System.ComponentModel.DataAnnotations;

namespace Purview.EventSourcing.AzureStorage.Cryptography;

/// <summary>
/// Options that configure the Azure Table Storage crypto key store.
/// </summary>
/// <remarks>
/// Bound from the <c>EventStore:AzureStorage:Cryptography</c> configuration section. When
/// <see cref="ConnectionString"/> is not supplied, the event-store connection string is used.
/// </remarks>
public sealed class AzureStorageEventCryptoKeyStoreOptions
{
	/// <summary>The configuration section name used to bind these options.</summary>
	public const string AzureStorageCryptography = "EventStore:AzureStorage:Cryptography";

	/// <summary>The default table name used to persist per-subject keys.</summary>
	public const string DefaultTableName = "EventStoreCryptoKeys";

	/// <summary>
	/// The storage connection string. When null, the event-store connection string is used.
	/// </summary>
	/// <remarks>
	/// Pointing this at a separate storage account is worth considering: the key table's retention
	/// and access policy is what decides how long an erasure takes to become irreversible, and it
	/// rarely wants the same treatment as the event data it protects.
	/// </remarks>
	public string? ConnectionString { get; set; }

	/// <summary>The name of the table that stores per-subject keys.</summary>
	/// <remarks>Azure table names are alphanumeric and must start with a letter.</remarks>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression("^[A-Za-z][A-Za-z0-9]{2,62}$")]
	public string TableName { get; set; } = DefaultTableName;
}
