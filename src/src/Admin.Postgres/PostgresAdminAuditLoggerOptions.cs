using System.ComponentModel.DataAnnotations;
using Purview.EventSourcing.Admin.Models;

namespace Purview.EventSourcing.Admin.Postgres;

/// <summary>
/// Options that configure the PostgreSQL admin audit logger.
/// </summary>
/// <remarks>
/// Bound from the <c>Admin:Postgres:Audit</c> configuration section. When
/// <see cref="ConnectionString"/> is not supplied, the event-store connection string is used.
/// </remarks>
public sealed class PostgresAdminAuditLoggerOptions
{
	/// <summary>The configuration section name used to bind these options.</summary>
	public const string PostgresAudit = "Admin:Postgres:Audit";

	/// <summary>The default table name used to persist audit entries.</summary>
	public const string DefaultTableName = "AdminAuditLog";

	/// <summary>
	/// The connection string used to access the PostgreSQL database. When null, the event-store
	/// connection string is used.
	/// </summary>
	/// <remarks>
	/// A separate database is worth considering: an audit trail usually wants different retention,
	/// different backup policy, and narrower write permissions than the data it describes.
	/// </remarks>
	public string? ConnectionString { get; set; }

	/// <summary>The schema that owns the audit table.</summary>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression(@"^[\w\-.]+$")]
	public string SchemaName { get; set; } = "public";

	/// <summary>The name of the table that stores audit entries.</summary>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression(@"^[\w\-.]+$")]
	public string TableName { get; set; } = DefaultTableName;

	/// <summary>When true, the audit table is created if it does not exist.</summary>
	public bool AutoCreateTable { get; set; } = true;

	/// <summary>
	/// What happens when an audit entry cannot be written.
	/// </summary>
	/// <remarks>
	/// Defaults to <see cref="AdminAuditFailureMode.Fail"/>. The Admin portal exposes event payloads
	/// and mutating operations, so an action nobody can prove happened is the outcome worth avoiding.
	/// Set to <see cref="AdminAuditFailureMode.Continue"/> where availability matters more.
	/// </remarks>
	public AdminAuditFailureMode FailureMode { get; set; } = AdminAuditFailureMode.Fail;
}
