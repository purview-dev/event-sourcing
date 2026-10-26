namespace Purview.EventSourcing.Tenancy;

/// <summary>
/// An <see cref="ICurrentTenant"/> backed by an ambient scope that flows across <see langword="await"/>.
/// </summary>
/// <remarks>
/// The default scope is <see cref="TenantScope.System"/>, which applies no tenant filter. That keeps a
/// single-tenant deployment working without configuration; a multi-tenant host registers its own
/// <see cref="ICurrentTenant"/> resolving the caller's tenant, and uses <see cref="BeginScope"/> only
/// where it deliberately changes tenant - a super user selecting one, or infrastructure work running as
/// <see cref="TenantScope.System"/>.
/// </remarks>
public sealed class AmbientCurrentTenant : ICurrentTenant
{
	static readonly AsyncLocal<TenantScope?> Ambient = new();

	/// <inheritdoc/>
	public TenantScope Current => Ambient.Value ?? TenantScope.System;

	/// <summary>
	/// Sets the ambient scope until the returned value is disposed.
	/// </summary>
	/// <param name="scope">The scope to apply.</param>
	/// <returns>A value that restores the previous scope when disposed.</returns>
	public static IDisposable BeginScope(TenantScope scope)
	{
		var previous = Ambient.Value;
		Ambient.Value = scope;

		return new Restore(previous);
	}

	sealed class Restore(TenantScope? previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
}
