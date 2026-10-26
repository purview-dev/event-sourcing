using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Security;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Admin.API.Auditing;

/// <summary>
/// Reads back what <see cref="InMemoryAdminAuditLogger"/> recorded, for tests and for a
/// single-process deployment that has not configured a durable audit store.
/// </summary>
/// <remarks>
/// <para>
/// It applies the same tenant rule as the durable readers - a tenant scope sees only its own
/// entries, <see cref="TenantScope.System"/> sees all of them, and an unowned entry is visible only
/// to System. That is the point of having it: a test written against the in-memory reader exercises
/// the same boundary the SQL readers enforce, rather than a permissive stand-in that would let a
/// leak reach production looking tested.
/// </para>
/// <para>
/// It lives here rather than beside <see cref="InMemoryAdminAuditLogger"/> in <c>Admin.Security</c>
/// only because it needs <see cref="ICurrentTenant"/>, and adding the core event-sourcing package to
/// <c>Admin.Security</c>'s dependency graph for one type is a worse trade than this asymmetry.
/// </para>
/// </remarks>
/// <param name="logger">The logger whose entries are read.</param>
/// <param name="currentTenant">Supplies the scope every read is filtered on.</param>
public sealed class InMemoryAdminAuditEntryReader(InMemoryAdminAuditLogger logger, ICurrentTenant currentTenant)
	: IAdminAuditEntryReader
{
	/// <inheritdoc/>
	public Task<AdminAuditPage> QueryAsync(AdminAuditQuery query, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(logger);

		var page = Math.Max(1, query.Page);
		var pageSize = Math.Clamp(query.PageSize, 1, AdminAuditQuery.MaximumPageSize);
		var scope = currentTenant.Current;

		var matched = logger
			.Entries.Where(entry =>
				scope.IsSystem || string.Equals(entry.TenantId, scope.TenantId, StringComparison.Ordinal)
			)
			.Where(entry => query.FromUtc is null || entry.TimestampUtc >= query.FromUtc)
			.Where(entry => query.ToUtc is null || entry.TimestampUtc < query.ToUtc)
			.Where(entry => query.Feature is null || entry.Feature == query.Feature)
			.Where(entry =>
				query.Principal is null || string.Equals(entry.Principal, query.Principal, StringComparison.Ordinal)
			)
			.Where(entry => query.Target is null || string.Equals(entry.Target, query.Target, StringComparison.Ordinal))
			.Where(entry => query.Succeeded is null || entry.Succeeded == query.Succeeded)
			.Where(entry =>
				query.CorrelationId is null
				|| string.Equals(entry.CorrelationId, query.CorrelationId, StringComparison.Ordinal)
			)
			.OrderByDescending(entry => entry.TimestampUtc)
			.Skip((page - 1) * pageSize)
			.Take(pageSize + 1)
			.ToList();

		var hasMore = matched.Count > pageSize;
		if (hasMore)
			matched.RemoveAt(matched.Count - 1);

		return Task.FromResult(new AdminAuditPage(matched, page, pageSize, hasMore));
	}
}
