using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.API.Endpoints;

namespace Microsoft.AspNetCore.Builder;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class WebApplicationExtensions
{
	extension([NotNull] WebApplication app)
	{
		/// <summary>
		/// Maps the Admin portal endpoints, honouring the enabled feature toggles in <see cref="AdminPortalOptions"/>.
		/// </summary>
		/// <param name="optionsAccessor">
		/// The options to configure the Admin portal with, or <see langword="null"/> to resolve them from the application's services.
		/// </param>
		/// <param name="configureEndpoints">Optional host authorization and endpoint conventions.</param>
		/// <remarks>
		/// <para>
		/// When <see cref="AdminPortalOptions.Enabled"/> is <see langword="false"/> no endpoints are mapped. All mapped
		/// endpoints require authorization and are grouped under <see cref="AdminPortalOptions.RoutePrefix"/>.
		/// </para>
		/// </remarks>
		public void MapPurviewEventSourcingAdminAPI(
			IOptions<AdminPortalOptions>? optionsAccessor = null,
			Action<AdminEndpointOptions>? configureEndpoints = null
		)
		{
			optionsAccessor ??= app.Services.GetRequiredService<IOptions<AdminPortalOptions>>();
			var options = optionsAccessor.Value;

			if (!options.Enabled)
				return;

			AdminEndpointOptions endpointOptions = new();
			configureEndpoints?.Invoke(endpointOptions);

			var group = app.MapGroup(options.RoutePrefix).WithName("AdminPortal").RequireAuthorization();
			endpointOptions.GroupConvention?.Invoke(group);

			// Map endpoint groups
			if (options.Features.SearchAggregates)
				ApplyConvention(
					AdminFeature.SearchAggregates,
					AdminAggregatesEndpoints.MapSearchAggregates(
						group,
						endpointOptions.GetPolicy(AdminFeature.SearchAggregates)
					),
					endpointOptions
				);

			if (options.Features.ViewAggregate)
				ApplyConvention(
					AdminFeature.ViewAggregate,
					AdminAggregatesEndpoints.MapViewAggregate(
						group,
						endpointOptions.GetPolicy(AdminFeature.ViewAggregate)
					),
					endpointOptions
				);

			if (options.Features.ViewEvents)
			{
				ApplyConvention(
					AdminFeature.ViewEvents,
					AdminAggregatesEndpoints.MapEventRange(group, endpointOptions.GetPolicy(AdminFeature.ViewEvents)),
					endpointOptions
				);

				if (options.Features.ExportEvents)
					ApplyConvention(
						AdminFeature.ExportEvents,
						AdminAggregatesEndpoints.MapExportEvents(
							group,
							endpointOptions.GetPolicy(AdminFeature.ExportEvents)
						),
						endpointOptions
					);
			}

			if (options.Features.ProjectPointInTime)
			{
				var policy = endpointOptions.GetPolicy(AdminFeature.ProjectPointInTime);
				ApplyConvention(
					AdminFeature.ProjectPointInTime,
					AdminAggregatesEndpoints.MapProjectionAtVersion(group, policy),
					endpointOptions
				);
				ApplyConvention(
					AdminFeature.ProjectPointInTime,
					AdminAggregatesEndpoints.MapProjectionAtTime(group, policy),
					endpointOptions
				);
			}

			if (options.Features.ViewCapabilities)
			{
				var policy = endpointOptions.GetPolicy(AdminFeature.ViewCapabilities);
				ApplyConvention(
					AdminFeature.ViewCapabilities,
					AdminOperationalEndpoints.MapCapabilities(group, policy),
					endpointOptions
				);
				ApplyConvention(
					AdminFeature.ViewCapabilities,
					AdminOperationalEndpoints.MapHealth(group, policy),
					endpointOptions
				);
			}

			if (options.Features.ViewPoisonedOutbox)
			{
				var policy = endpointOptions.GetPolicy(AdminFeature.ViewPoisonedOutbox);
				ApplyConvention(
					AdminFeature.ViewPoisonedOutbox,
					AdminOperationalEndpoints.MapPoisonedOutbox(group, policy),
					endpointOptions
				);
			}

			if (options.Features.ViewManifest)
			{
				var policy = endpointOptions.GetPolicy(AdminFeature.ViewManifest);
				ApplyConvention(
					AdminFeature.ViewManifest,
					AdminOperationalEndpoints.MapManifest(group, policy),
					endpointOptions
				);
			}

			if (options.Features.ViewUnknownEvents)
			{
				var policy = endpointOptions.GetPolicy(AdminFeature.ViewUnknownEvents);
				ApplyConvention(
					AdminFeature.ViewUnknownEvents,
					AdminOperationalEndpoints.MapUnknownEvents(group, policy),
					endpointOptions
				);
			}

			if (options.Features.ViewSnapshot)
			{
				var policy = endpointOptions.GetPolicy(AdminFeature.ViewSnapshot);
				ApplyConvention(
					AdminFeature.ViewSnapshot,
					AdminSnapshotEndpoints.MapSnapshotStatus(group, policy),
					endpointOptions
				);
			}

			if (options.Features.RebuildSnapshot)
			{
				var policy = endpointOptions.GetPolicy(AdminFeature.RebuildSnapshot);
				ApplyConvention(
					AdminFeature.RebuildSnapshot,
					AdminSnapshotEndpoints.MapRebuildSnapshot(group, policy),
					endpointOptions
				);
			}
		}
	}

	static void ApplyConvention(AdminFeature feature, RouteHandlerBuilder endpoint, AdminEndpointOptions options) =>
		options.EndpointConvention?.Invoke(feature, endpoint);
}
