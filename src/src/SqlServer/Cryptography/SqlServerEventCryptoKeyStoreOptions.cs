using System.ComponentModel.DataAnnotations;

namespace Purview.EventSourcing.SqlServer.Cryptography;

/// <summary>
/// Options that configure the SQL Server crypto key store.
/// </summary>
/// <remarks>
/// Bound from the <c>EventStore:SqlServer:Cryptography</c> configuration section. When
/// <see cref="ConnectionString"/> is not supplied, the event-store connection string is used.
/// </remarks>
public sealed class SqlServerEventCryptoKeyStoreOptions
{
	/// <summary>The configuration section name used to bind these options.</summary>
	public const string SqlServerCryptography = "EventStore:SqlServer:Cryptography";

	/// <summary>The default table name used to persist per-subject keys.</summary>
	public const string DefaultTableName = "EventStoreCryptoKeys";

	/// <summary>
	/// The connection string used to access the SQL Server database. When null, the event-store
	/// connection string is used.
	/// </summary>
	/// <remarks>
	/// Pointing this at a separate database is worth considering: the key table's backup policy is
	/// what decides how long an erasure takes to become irreversible, and it rarely wants the same
	/// retention as the event data it protects.
	/// </remarks>
	public string? ConnectionString { get; set; }

	/// <summary>The schema that owns the key table.</summary>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression(@"^[\w\-.]+$")]
	public string SchemaName { get; set; } = "dbo";

	/// <summary>The name of the table that stores per-subject keys.</summary>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression(@"^[\w\-.]+$")]
	public string TableName { get; set; } = DefaultTableName;

	/// <summary>When true, the key table is created if it does not exist.</summary>
	public bool AutoCreateTable { get; set; } = true;
}
