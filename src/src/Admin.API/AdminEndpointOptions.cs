using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Security;

namespace Purview.EventSourcing.Admin.API;

/// <summary>
/// Configures host-owned authorization policies and conventions for Admin API endpoints.
/// </summary>
public sealed class AdminEndpointOptions
{
	readonly Dictionary<AdminFeature, string> _policies = new()
	{
		[AdminFeature.SearchAggregates] = AdminPortalPolicies.SearchAggregates,
		[AdminFeature.ViewAggregate] = AdminPortalPolicies.ViewAggregate,
		[AdminFeature.ViewEvents] = AdminPortalPolicies.ViewEvents,
		[AdminFeature.ViewEventPayloads] = AdminPortalPolicies.ViewEventPayloads,
		[AdminFeature.ProjectPointInTime] = AdminPortalPolicies.ProjectPointInTime,
		[AdminFeature.ExportEvents] = AdminPortalPolicies.ExportEvents,
		[AdminFeature.ViewCapabilities] = AdminPortalPolicies.ViewCapabilities,
		[AdminFeature.ViewPoisonedOutbox] = AdminPortalPolicies.ViewPoisonedOutbox,
		[AdminFeature.ViewManifest] = AdminPortalPolicies.ViewManifest,
		[AdminFeature.ViewUnknownEvents] = AdminPortalPolicies.ViewUnknownEvents,
		[AdminFeature.ViewSnapshot] = AdminPortalPolicies.ViewSnapshot,
		[AdminFeature.RebuildSnapshot] = AdminPortalPolicies.RebuildSnapshot,
	};

	/// <summary>
	/// Gets or sets a convention applied once to the Admin route group.
	/// </summary>
	public Action<RouteGroupBuilder>? GroupConvention { get; set; }

	/// <summary>
	/// Gets or sets a convention applied to every mapped endpoint with its associated feature.
	/// </summary>
	public Action<AdminFeature, RouteHandlerBuilder>? EndpointConvention { get; set; }

	/// <summary>
	/// Assigns a host authorization policy to an Admin feature.
	/// </summary>
	public void RequirePolicy(AdminFeature feature, string policyName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
		_policies[feature] = policyName;
	}

	internal string GetPolicy(AdminFeature feature) => _policies[feature];
}
