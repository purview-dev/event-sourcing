namespace Purview.EventSourcing.Admin.Models;

/// <summary>
/// A record of a privileged Admin portal operation for audit.
/// </summary>
/// <param name="TimestampUtc">The UTC time the operation occurred.</param>
/// <param name="Feature">The <see cref="AdminFeature"/> being exercised.</param>
/// <param name="Action">A short action description, for example "Read" or "Rebuild".</param>
/// <param name="Principal">The authenticated principal name, when known.</param>
/// <param name="Target">The operation target (for example an aggregate id), when applicable.</param>
/// <param name="Succeeded">Whether the operation succeeded.</param>
/// <param name="Details">Optional details (for example a failure reason).</param>
public sealed record AdminAuditEntry(
	DateTimeOffset TimestampUtc,
	AdminFeature Feature,
	string Action,
	string? Principal,
	string? Target,
	bool Succeeded,
	string? Details
)
{
	/// <summary>
	/// The tenant the operation acted on, or <see langword="null"/> in a single-tenant deployment.
	/// </summary>
	/// <remarks>
	/// "Who did this" is incomplete without "to which tenant" - the same administrator may be
	/// entitled to several. Stamped centrally from the request's tenant scope rather than at each
	/// call site, so an endpoint cannot forget it.
	/// </remarks>
	public string? TenantId { get; init; }

	/// <summary>
	/// Correlates the entry with the request and with any events the operation produced.
	/// </summary>
	/// <remarks>
	/// Without it an audit entry and the events it caused cannot be tied together after the fact,
	/// which is most of what an investigation needs.
	/// </remarks>
	public string? CorrelationId { get; init; }
}
