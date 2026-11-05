namespace Purview.EventSourcing.Admin.Models;

/// <summary>
/// Narrows an <see cref="IAdminAuditEntryReader"/> read. Every member is optional; an empty query
/// returns the most recent entries the caller is allowed to see.
/// </summary>
/// <remarks>
/// <para>
/// Ordering is fixed: newest first. An audit read is almost always "what happened recently", and a
/// configurable order would let a caller page from the oldest end and quietly miss entries written
/// while they paged.
/// </para>
/// <para>
/// There is deliberately no tenant filter here. The tenant is taken from the ambient
/// <c>ICurrentTenant</c>, not from the caller, so a caller cannot widen their own scope by asking
/// for a different tenant.
/// </para>
/// </remarks>
public sealed record AdminAuditQuery
{
	/// <summary>The inclusive lower bound on <see cref="AdminAuditEntry.TimestampUtc"/>.</summary>
	public DateTimeOffset? FromUtc { get; init; }

	/// <summary>The exclusive upper bound on <see cref="AdminAuditEntry.TimestampUtc"/>.</summary>
	public DateTimeOffset? ToUtc { get; init; }

	/// <summary>Restricts the read to one feature.</summary>
	public AdminFeature? Feature { get; init; }

	/// <summary>Restricts the read to one principal, matched exactly.</summary>
	public string? Principal { get; init; }

	/// <summary>Restricts the read to one operation target, matched exactly.</summary>
	public string? Target { get; init; }

	/// <summary>Restricts the read to successful or failed operations.</summary>
	public bool? Succeeded { get; init; }

	/// <summary>Correlates with a request and the events it produced.</summary>
	public string? CorrelationId { get; init; }

	/// <summary>The one-based page number.</summary>
	public int Page { get; init; } = 1;

	/// <summary>The page size. Implementations clamp this to <see cref="MaximumPageSize"/>.</summary>
	public int PageSize { get; init; } = 50;

	/// <summary>
	/// The largest page an implementation will return, whatever <see cref="PageSize"/> asks for.
	/// </summary>
	/// <remarks>
	/// An audit table is the one table that only grows, so an unbounded page is a way to pull the
	/// whole trail - including every principal and target it names - in one request.
	/// </remarks>
	public const int MaximumPageSize = 500;
}
