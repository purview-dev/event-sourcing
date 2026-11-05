namespace Purview.EventSourcing.Admin.Models;

/// <summary>
/// Represents a permission granted to a user for an admin portal feature.
/// </summary>
/// <param name="Feature">The admin feature the permission applies to.</param>
/// <param name="AggregateType">The aggregate type the permission is scoped to, or <see langword="null"/> to apply to all aggregate types.</param>
/// <param name="Allowed">A value indicating whether access is allowed (<see langword="true"/>) or denied (<see langword="false"/>).</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
public sealed record AdminPermission(
	AdminFeature Feature,
	string? AggregateType, // null = applies to all aggregate types
	bool Allowed
);
