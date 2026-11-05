using System.Security.Claims;
using Purview.EventSourcing.Admin.Failures;
using Purview.EventSourcing.Admin.Services;
using Purview.Results;

namespace Purview.EventSourcing.Admin.Security.Providers;

/// <summary>
/// Resolves the admin tenant from the signed-in user's tenant claims.
/// </summary>
/// <remarks>
/// Every tenant the administrator may act for is a separate claim of the configured type. One claim means
/// the administrator is scoped to it and may not name another; several means they must choose.
/// </remarks>
/// <param name="claimType">The claim carrying an entitled tenant identifier.</param>
public sealed class ClaimsAdminTenantResolver(string claimType = ClaimsAdminTenantResolver.DefaultClaimType)
	: IAdminTenantResolver
{
	/// <summary>
	/// The claim type used when none is configured.
	/// </summary>
	public const string DefaultClaimType = "tenant";

	/// <inheritdoc/>
	public Task<Result<string, AdminTenantFailure>> ResolveAsync(
		ClaimsPrincipal user,
		string? requestedTenantId,
		CancellationToken cancellationToken
	)
	{
		ArgumentNullException.ThrowIfNull(user);

		var entitled = user.FindAll(claimType)
			.Select(claim => claim.Value)
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

		if (!string.IsNullOrWhiteSpace(requestedTenantId))
		{
			return Task.FromResult(
				entitled.Contains(requestedTenantId)
					? Result<string, AdminTenantFailure>.Success(requestedTenantId)
					: AdminTenantFailure.Failure<string>(new AdminTenantForbidden(userId, requestedTenantId))
			);
		}

		// Exactly one entitlement needs no choice. Anything else - none, or several - is a denial:
		// defaulting to the first would silently pick a tenant, and the system scope is not a user's
		// to assume.
		return Task.FromResult(
			entitled.Count is 1
				? Result<string, AdminTenantFailure>.Success(entitled.First())
				: AdminTenantFailure.Failure<string>(new AdminTenantNotSelected(userId))
		);
	}
}
