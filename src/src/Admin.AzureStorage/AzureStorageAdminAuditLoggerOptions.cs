using Purview.EventSourcing.Admin.Models;

namespace Purview.EventSourcing.Admin.AzureStorage;

/// <summary>
/// Configures <see cref="AzureStorageAdminAuditLogger"/> and
/// <see cref="AzureStorageAdminAuditEntryReader"/>.
/// </summary>
public sealed class AzureStorageAdminAuditLoggerOptions
{
	/// <summary>The configuration section these options bind from.</summary>
	public const string AzureStorageAudit = "Admin:AzureStorage:Audit";

	/// <summary>
	/// The storage connection string. Falls back to the event store's when not set.
	/// </summary>
	public string? ConnectionString { get; set; }

	/// <summary>The table audit entries are written to.</summary>
	public string TableName { get; set; } = "AdminAuditLog";

	/// <summary>
	/// What happens when an audit entry cannot be written.
	/// </summary>
	/// <remarks>
	/// <see cref="AdminAuditFailureMode.Fail"/> by default, matching the other durable loggers: an
	/// action nobody can prove happened is worse than an action that did not.
	/// </remarks>
	public AdminAuditFailureMode FailureMode { get; set; } = AdminAuditFailureMode.Fail;

	/// <summary>Whether to create the table on first use.</summary>
	public bool AutoCreateTable { get; set; } = true;
}
