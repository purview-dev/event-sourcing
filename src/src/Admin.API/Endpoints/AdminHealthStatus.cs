namespace Purview.EventSourcing.Admin.API.Endpoints;

/// <summary>
/// The values reported by <see cref="AdminHealthResponse.Status"/>.
/// </summary>
public static class AdminHealthStatus
{
	/// <summary>Storage responded to the probe.</summary>
	public const string Healthy = "Healthy";

	/// <summary>Storage did not respond, or the probe failed.</summary>
	public const string Unhealthy = "Unhealthy";

	/// <summary>
	/// No <see cref="Services.IAdminStorageProbe"/> is registered, so storage was not probed. The
	/// response still describes the capability contract.
	/// </summary>
	public const string Unknown = "Unknown";
}
