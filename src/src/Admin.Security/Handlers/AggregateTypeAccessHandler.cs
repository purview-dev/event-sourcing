using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Purview.EventSourcing.Admin.Security.Requirements;
using Purview.EventSourcing.Admin.Services;

namespace Purview.EventSourcing.Admin.Security.Handlers;

/// <summary>
/// Authorization handler that verifies the user has general aggregate access.
/// </summary>
/// <remarks>
/// <para>
/// This handler delegates the actual aggregate-type checks to endpoint-level logic which has access to route
/// parameters. It only verifies that the user holds at least one granted permission (or no explicit denials),
/// while deny-by-default and per-aggregate-type checks are enforced by the endpoint layer.
/// </para>
/// </remarks>
/// <param name="permissionProvider">The permission provider used to resolve the current user's permissions.</param>
public sealed class AggregateTypeAccessHandler(IAdminPermissionProvider permissionProvider)
	: AuthorizationHandler<AggregateTypeAccessRequirement>
{
	///<inheritdoc/>
	protected override async Task HandleRequirementAsync(
		[NotNull] AuthorizationHandlerContext context,
		AggregateTypeAccessRequirement requirement
	)
	{
		// This handler delegates the actual aggregate type checking to endpoint-level logic
		// which has access to route parameters. For now, we just check that general
		// aggregate access is allowed, and route-specific checks happen in endpoint filters.
		var permissions = await permissionProvider.GetPermissionsAsync(context.User, CancellationToken.None);

		// If no explicit aggregate-type-scoped denial exists, allow (the endpoint filter will do fine-grained checks)
		var hasSomePermission = permissions.Any(p => p.Allowed);

		if (hasSomePermission || permissions.Count == 0)
		{
			// Either user has some permission, or we use deny-by-default at endpoint level
			context.Succeed(requirement);
		}
		else
		{
			context.Fail();
		}
	}
}
