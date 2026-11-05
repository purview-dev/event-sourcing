using System.Security.Claims;
using Purview.EventSourcing.Admin.Models;

namespace Purview.EventSourcing.Admin.Services;

/// <summary>
/// Pluggable permission provider for admin portal.
/// Deny-by-default: no matching allow = deny.
/// </summary>
public interface IAdminPermissionProvider
{
	/// <summary>
	/// Gets the permissions granted to the specified user.
	/// </summary>
	/// <param name="user">The user whose permissions should be resolved.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>The permissions granted to the user.</returns>
	Task<IReadOnlyList<AdminPermission>> GetPermissionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}
