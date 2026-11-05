using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Contracts;

/// <summary>
/// An <see cref="ICurrentTenant"/> fixed to one scope, for the tenancy contract tests.
/// </summary>
/// <remarks>
/// Named to match the <c>Contracts\EventStore*.cs</c> link glob the provider test projects use, so it
/// travels with the contract suite rather than needing a reference in each project.
/// </remarks>
/// <param name="scope">The scope to report.</param>
public sealed class EventStoreTenantScopeStub(TenantScope scope) : ICurrentTenant
{
	/// <inheritdoc/>
	public TenantScope Current => scope;
}
