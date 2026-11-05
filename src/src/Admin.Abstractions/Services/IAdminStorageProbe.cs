namespace Purview.EventSourcing.Admin.Services;

/// <summary>
/// Probes the configured event-store storage so the Admin portal can report whether it is reachable.
/// </summary>
/// <remarks>
/// Implemented by the provider-specific <c>Admin.*</c> adapter, because only it knows how to reach the
/// storage cheaply. The probe must be read-only and side-effect free: it runs on an operational endpoint
/// that may be polled frequently.
/// </remarks>
public interface IAdminStorageProbe
{
	/// <summary>
	/// Checks that the configured storage is reachable.
	/// </summary>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns><see langword="true"/> when storage responded; otherwise <see langword="false"/>.</returns>
	Task<bool> IsReachableAsync(CancellationToken cancellationToken);
}
