using System.ComponentModel.DataAnnotations;

namespace Purview.EventSourcing.SqlServer.Subscriptions;

/// <summary>
/// Options that configure the SQL Server subscription checkpoint store.
/// </summary>
/// <remarks>
/// Bound from the <c>EventStore:SqlServer:Subscriptions</c> configuration section. When
/// <see cref="ConnectionString"/> is not supplied, the event-store connection string is used.
/// </remarks>
public sealed class SqlServerSubscriptionCheckpointStoreOptions
{
	/// <summary>The configuration section name used to bind these options.</summary>
	public const string SqlServerSubscriptions = "EventStore:SqlServer:Subscriptions";

	/// <summary>The default table name used to persist subscription positions.</summary>
	public const string DefaultTableName = "EventStoreSubscriptionCheckpoints";

	/// <summary>
	/// The connection string used to access the SQL Server database. When null, the event-store
	/// connection string is used.
	/// </summary>
	public string? ConnectionString { get; set; }

	/// <summary>The schema that owns the checkpoint table.</summary>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression(@"^[\w\-.]+$")]
	public string SchemaName { get; set; } = "dbo";

	/// <summary>The name of the table that stores subscription positions.</summary>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression(@"^[\w\-.]+$")]
	public string TableName { get; set; } = DefaultTableName;

	/// <summary>When true, the checkpoint table is created if it does not exist.</summary>
	public bool AutoCreateTable { get; set; } = true;
}
