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
);
