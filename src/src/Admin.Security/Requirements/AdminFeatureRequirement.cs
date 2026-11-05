using Microsoft.AspNetCore.Authorization;
using Purview.EventSourcing.Admin.Models;

namespace Purview.EventSourcing.Admin.Security.Requirements;

/// <summary>
/// Authorization requirement that indicates the user must be granted the specified admin portal feature.
/// </summary>
/// <param name="feature">The admin feature the user must be permitted to use.</param>
public sealed class AdminFeatureRequirement(AdminFeature feature) : IAuthorizationRequirement
{
	/// <summary>
	/// Gets the admin feature that is required.
	/// </summary>
	public AdminFeature Feature => feature;
}
