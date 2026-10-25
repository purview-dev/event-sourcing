using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Purview.EventSourcing.Admin.Security.Requirements;
using Purview.EventSourcing.Admin.Services;

namespace Purview.EventSourcing.Admin.Security.Handlers;

/// <summary>
/// Authorization handler that grants a requirement only when the user holds an explicit
/// allowed permission for the feature.
/// </summary>
/// <param name="permissionProvider">The permission provider used to resolve the current user's permissions.</param>
public sealed class AdminFeatureAuthorizationHandler(IAdminPermissionProvider permissionProvider)
	: AuthorizationHandler<AdminFeatureRequirement>
{
	///<inheritdoc/>
	protected override async Task HandleRequirementAsync(
		[NotNull] AuthorizationHandlerContext context,
		AdminFeatureRequirement requirement
	)
	{
		ArgumentNullException.ThrowIfNull(requirement);
		var permissions = await permissionProvider.GetPermissionsAsync(context.User, CancellationToken.None);

		var aggregateType = context.Resource as string;
		var hasPermission = false;
		foreach (var permission in permissions)
		{
			if (
				permission.Feature != requirement.Feature
				|| (permission.AggregateType is not null && permission.AggregateType != aggregateType)
			)
				continue;

			if (!permission.Allowed)
			{
				hasPermission = false;
				break;
			}

			hasPermission = true;
		}

		if (hasPermission)
			context.Succeed(requirement);
		else
			context.Fail();
	}
}
