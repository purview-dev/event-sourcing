using Purview.EventSourcing.Admin.Models;

namespace Purview.EventSourcing.Admin.MongoDB;

/// <summary>
/// Configures <see cref="MongoDBAdminAuditLogger"/> and <see cref="MongoDBAdminAuditEntryReader"/>.
/// </summary>
public sealed class MongoDBAdminAuditLoggerOptions
{
	/// <summary>The configuration section these options bind from.</summary>
	public const string MongoDBAudit = "Admin:MongoDB:Audit";

	/// <summary>The database holding the audit collection. Defaults to the event-store database.</summary>
	public string DatabaseName { get; set; } = "EventStore";

	/// <summary>The collection audit entries are written to.</summary>
	public string CollectionName { get; set; } = "AdminAuditLog";

	/// <summary>
	/// What happens when an audit entry cannot be written.
	/// </summary>
	/// <remarks>
	/// <see cref="AdminAuditFailureMode.Fail"/> by default, matching the SQL loggers: an action
	/// nobody can prove happened is worse than an action that did not.
	/// </remarks>
	public AdminAuditFailureMode FailureMode { get; set; } = AdminAuditFailureMode.Fail;

	/// <summary>
	/// Whether to create the index on first use.
	/// </summary>
	/// <remarks>
	/// Mongo creates a collection implicitly on first insert, so there is no table to create - only
	/// the index the reader's tenant-and-time query depends on.
	/// </remarks>
	public bool AutoCreateIndex { get; set; } = true;
}
