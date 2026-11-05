using System.Security.Claims;
using Purview.EventSourcing.Admin.Failures;
using Purview.Results;

namespace Purview.EventSourcing.Admin.Services;

/// <summary>
/// Decides which tenant an admin request runs against.
/// </summary>
/// <remarks>
/// An administrator entitled to a single tenant is scoped to it. One entitled to several - a super user -
/// must name the tenant on the request; naming none is a denial, not permission to see every tenant. The
/// system scope is deliberately unreachable from here - the contract returns a tenant identifier and
/// nothing else - because crossing tenants belongs to infrastructure, not to a signed-in user.
/// </remarks>
public interface IAdminTenantResolver
{
	/// <summary>
	/// Resolves the tenant scope for a request.
	/// </summary>
	/// <param name="user">The signed-in administrator.</param>
	/// <param name="requestedTenantId">The tenant named on the request, if any.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>The tenant identifier to scope to, or the reason the request is denied.</returns>
	Task<Result<string, AdminTenantFailure>> ResolveAsync(
		ClaimsPrincipal user,
		string? requestedTenantId,
		CancellationToken cancellationToken
	);
}
