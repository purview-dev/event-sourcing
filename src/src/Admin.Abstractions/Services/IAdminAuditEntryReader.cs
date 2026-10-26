using Purview.EventSourcing.Admin.Models;

namespace Purview.EventSourcing.Admin.Services;

/// <summary>
/// Reads back what <see cref="IAdminAuditLogger"/> wrote.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the logger on purpose. Writing the audit trail is something every Admin request
/// does; reading it is a privileged investigation, and a deployment can grant the two to different
/// database principals only if they are different types. It also keeps
/// <see cref="IAdminAuditLogger"/> implementable by a write-only sink - a log pipeline, an event hub
/// - that has no read side at all.
/// </para>
/// <para>
/// <strong>Implementations must filter on the ambient <c>ICurrentTenant</c> and must fail
/// closed.</strong> An entry with no <see cref="AdminAuditEntry.TenantId"/> is visible only to
/// <c>TenantScope.System</c>; it is never treated as visible to everyone. An audit trail that leaks
/// across tenants discloses the principals, targets and timings of another tenant's administration,
/// which is worse than disclosing its data.
/// </para>
/// <para>
/// There is no HTTP endpoint for this in the Admin API. Reading the audit trail is a second
/// privilege, and which principals hold it is a deployment decision rather than a framework default;
/// the seam is here so an application can expose it on terms it chooses.
/// </para>
/// </remarks>
public interface IAdminAuditEntryReader
{
	/// <summary>Reads one page of audit entries, newest first.</summary>
	/// <param name="query">Narrows the read. Tenant scope is not part of it.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	Task<AdminAuditPage> QueryAsync(AdminAuditQuery query, CancellationToken cancellationToken);
}
