using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Security.Requirements;

namespace Microsoft.AspNetCore.Authorization;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class AuthorizationBuilderExtensions
{
	extension([NotNull] AuthorizationBuilder builder)
	{
		/// <summary>
		/// Adds the admin portal authorization policies to the authorization builder.
		/// </summary>
		/// <returns>The configured authorization builder for chaining.</returns>
		public AuthorizationBuilder AddPurviewEventSourcingAdminPolicies()
		{
			builder.AddPolicy(
				AdminPortalPolicies.SearchAggregates,
				policy => policy.AddRequirements(new AdminFeatureRequirement(AdminFeature.SearchAggregates))
			);

			builder.AddPolicy(
				AdminPortalPolicies.ViewAggregate,
				policy =>
					policy.AddRequirements(
						new AdminFeatureRequirement(AdminFeature.ViewAggregate),
						new AggregateTypeAccessRequirement()
					)
			);

			builder.AddPolicy(
				AdminPortalPolicies.ViewEvents,
				policy =>
					policy.AddRequirements(
						new AdminFeatureRequirement(AdminFeature.ViewEvents),
						new AggregateTypeAccessRequirement()
					)
			);

			builder.AddPolicy(
				AdminPortalPolicies.ViewEventPayloads,
				policy => policy.AddRequirements(new AdminFeatureRequirement(AdminFeature.ViewEventPayloads))
			);

			builder.AddPolicy(
				AdminPortalPolicies.ProjectPointInTime,
				policy =>
					policy.AddRequirements(
						new AdminFeatureRequirement(AdminFeature.ProjectPointInTime),
						new AggregateTypeAccessRequirement()
					)
			);

			builder.AddPolicy(
				AdminPortalPolicies.ExportEvents,
				policy =>
					policy.AddRequirements(
						new AdminFeatureRequirement(AdminFeature.ExportEvents),
						new AdminFeatureRequirement(AdminFeature.ViewEventPayloads),
						new AggregateTypeAccessRequirement()
					)
			);

			builder.AddPolicy(
				AdminPortalPolicies.ViewCapabilities,
				policy => policy.AddRequirements(new AdminFeatureRequirement(AdminFeature.ViewCapabilities))
			);

			builder.AddPolicy(
				AdminPortalPolicies.ViewPoisonedOutbox,
				policy => policy.AddRequirements(new AdminFeatureRequirement(AdminFeature.ViewPoisonedOutbox))
			);

			builder.AddPolicy(
				AdminPortalPolicies.ViewManifest,
				policy => policy.AddRequirements(new AdminFeatureRequirement(AdminFeature.ViewManifest))
			);

			builder.AddPolicy(
				AdminPortalPolicies.ViewUnknownEvents,
				policy =>
					policy.AddRequirements(
						new AdminFeatureRequirement(AdminFeature.ViewUnknownEvents),
						new AggregateTypeAccessRequirement()
					)
			);

			builder.AddPolicy(
				AdminPortalPolicies.ViewSnapshot,
				policy =>
					policy.AddRequirements(
						new AdminFeatureRequirement(AdminFeature.ViewSnapshot),
						new AggregateTypeAccessRequirement()
					)
			);

			builder.AddPolicy(
				AdminPortalPolicies.RebuildSnapshot,
				policy =>
					policy.AddRequirements(
						new AdminFeatureRequirement(AdminFeature.RebuildSnapshot),
						new AggregateTypeAccessRequirement()
					)
			);

			return builder;
		}
	}
}
