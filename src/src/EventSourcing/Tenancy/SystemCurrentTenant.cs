namespace Purview.EventSourcing.Tenancy;

/// <summary>
/// An <see cref="ICurrentTenant"/> that always reports <see cref="TenantScope.System"/>.
/// </summary>
/// <remarks>
/// The infrastructure scope: it applies no tenant filter, so it reads and writes across every tenant.
/// Use it for a single-tenant deployment, and for background work that legitimately spans tenants.
/// Do not register it as the resolver in a multi-tenant host serving user requests - every request would
/// then see every tenant's data.
/// </remarks>
public sealed class SystemCurrentTenant : ICurrentTenant
{
	/// <inheritdoc/>
	public TenantScope Current => TenantScope.System;
}
