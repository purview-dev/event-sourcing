using System.Security.Claims;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;

namespace Purview.EventSourcing.Samples.Web.Services;

sealed class SampleAdminPermissionProvider : IAdminPermissionProvider
{
	static readonly IReadOnlyList<AdminPermission> Permissions =
	[
		new(AdminFeature.SearchAggregates, AggregateType: null, Allowed: true),
		new(AdminFeature.ViewAggregate, AggregateType: null, Allowed: true),
		new(AdminFeature.ViewEvents, AggregateType: null, Allowed: true),
		new(AdminFeature.ViewEventPayloads, AggregateType: null, Allowed: true),
		new(AdminFeature.ProjectPointInTime, AggregateType: null, Allowed: true),
		new(AdminFeature.ExportEvents, AggregateType: null, Allowed: true),
	];

	public Task<IReadOnlyList<AdminPermission>> GetPermissionsAsync(
		ClaimsPrincipal user,
		CancellationToken cancellationToken
	) => Task.FromResult(Permissions);
}
