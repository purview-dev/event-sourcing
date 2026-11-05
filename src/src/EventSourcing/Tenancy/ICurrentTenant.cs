namespace Purview.EventSourcing.Tenancy;

/// <summary>
/// Supplies the <see cref="TenantScope"/> the current operation runs under.
/// </summary>
/// <remarks>
/// Resolved per operation, so a request-scoped implementation can return the caller's tenant while
/// background work runs as <see cref="TenantScope.System"/>. The event store applies the scope as a
/// filter on reads and stamps it on writes.
/// </remarks>
public interface ICurrentTenant
{
	/// <summary>
	/// Gets the scope for the operation in progress.
	/// </summary>
	TenantScope Current { get; }
}
