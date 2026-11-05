namespace Purview.EventSourcing.Admin.Models;

/// <summary>
/// One page of audit entries, newest first.
/// </summary>
/// <param name="Entries">The entries on this page.</param>
/// <param name="Page">The one-based page number this result came from.</param>
/// <param name="PageSize">The page size actually applied, which may be smaller than requested.</param>
/// <param name="HasMore">Whether at least one more entry exists beyond this page.</param>
/// <remarks>
/// There is no total count. Counting an append-only table that only grows costs a scan on every
/// page, and the number is stale before the caller reads it; <paramref name="HasMore"/> is what a
/// pager actually needs.
/// </remarks>
public sealed record AdminAuditPage(IReadOnlyList<AdminAuditEntry> Entries, int Page, int PageSize, bool HasMore)
{
	/// <summary>An empty page.</summary>
	public static AdminAuditPage Empty { get; } = new([], 1, 0, false);
}
