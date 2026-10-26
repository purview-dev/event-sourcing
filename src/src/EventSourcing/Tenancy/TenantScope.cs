namespace Purview.EventSourcing.Tenancy;

/// <summary>
/// The tenant an operation runs under.
/// </summary>
/// <remarks>
/// Either a single tenant, or <see cref="System"/> for infrastructure work that deliberately crosses
/// tenants. The distinction is explicit rather than a <see langword="null"/> tenant id, so a resolver
/// that fails to determine a tenant cannot be mistaken for permission to read every tenant.
/// </remarks>
public readonly record struct TenantScope
{
	TenantScope(string? tenantId, bool isSystem)
	{
		TenantId = tenantId;
		IsSystem = isSystem;
	}

	/// <summary>
	/// Crosses every tenant. Use for infrastructure operations - projection rebuilds, migrations,
	/// outbox dispatch - and for a single-tenant deployment, where no tenant filter applies.
	/// </summary>
	public static TenantScope System { get; } = new(null, isSystem: true);

	/// <summary>
	/// The tenant identifier, or <see langword="null"/> when this is <see cref="System"/>.
	/// </summary>
	public string? TenantId { get; }

	/// <summary>
	/// <see langword="true"/> when the scope crosses every tenant.
	/// </summary>
	public bool IsSystem { get; }

	/// <summary>
	/// Creates a scope limited to a single tenant.
	/// </summary>
	/// <param name="tenantId">The tenant identifier.</param>
	/// <exception cref="ArgumentException">Thrown when <paramref name="tenantId"/> is blank.</exception>
	public static TenantScope For(string tenantId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

		return new(tenantId, isSystem: false);
	}
}
