namespace Purview.EventSourcing.Tenancy;

/// <summary>
/// Thrown when an operation runs under a tenant scope that the storage provider cannot enforce.
/// </summary>
/// <remarks>
/// A configuration fault, not an outcome a caller can act on, so it throws rather than returning a
/// failure case. Only the SQL Server and PostgreSQL providers isolate tenants; the others would ignore
/// the scope, and silently serving one tenant's request from every tenant's data is the failure this
/// prevents.
/// </remarks>
public sealed class TenantScopeNotSupportedException : InvalidOperationException
{
	/// <summary>
	/// Constructs the exception for a provider and tenant.
	/// </summary>
	/// <param name="providerName">The storage provider that cannot enforce the scope.</param>
	/// <param name="tenantId">The tenant the operation was scoped to.</param>
	public TenantScopeNotSupportedException(string providerName, string? tenantId)
		: base(
			$"The '{providerName}' provider does not enforce tenant isolation, so it cannot serve an operation scoped to tenant '{tenantId}'. "
				+ "Use the SQL Server or PostgreSQL provider for multi-tenant storage, or run this operation under TenantScope.System."
		)
	{
		ProviderName = providerName;
		TenantId = tenantId;
	}

	/// <summary>Constructs the exception.</summary>
	public TenantScopeNotSupportedException() { }

	/// <summary>Constructs the exception with a message.</summary>
	/// <param name="message">The message.</param>
	public TenantScopeNotSupportedException(string message)
		: base(message) { }

	/// <summary>Constructs the exception with a message and inner exception.</summary>
	/// <param name="message">The message.</param>
	/// <param name="innerException">The inner exception.</param>
	public TenantScopeNotSupportedException(string message, Exception innerException)
		: base(message, innerException) { }

	/// <summary>The storage provider that cannot enforce the scope.</summary>
	public string? ProviderName { get; }

	/// <summary>The tenant the operation was scoped to.</summary>
	public string? TenantId { get; }

	/// <summary>
	/// Throws when <paramref name="scope"/> names a tenant.
	/// </summary>
	/// <param name="scope">The scope the operation is running under.</param>
	/// <param name="providerName">The storage provider.</param>
	public static void ThrowIfScoped(TenantScope scope, string providerName)
	{
		if (!scope.IsSystem)
			throw new TenantScopeNotSupportedException(providerName, scope.TenantId);
	}
}
