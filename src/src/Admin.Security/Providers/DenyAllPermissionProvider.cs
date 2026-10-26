using System.Security.Claims;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;

namespace Purview.EventSourcing.Admin.Security.Providers;

/// <summary>
/// Default deny-by-default permission provider.
/// Returns empty list (all denied) unless explicitly overridden.
/// </summary>
public sealed class DenyAllPermissionProvider : IAdminPermissionProvider
{
	///<inheritdoc/>
	public Task<IReadOnlyList<AdminPermission>> GetPermissionsAsync(
		ClaimsPrincipal user,
		CancellationToken cancellationToken
	)
	{
		return Task.FromResult<IReadOnlyList<AdminPermission>>([]);
	}
}
