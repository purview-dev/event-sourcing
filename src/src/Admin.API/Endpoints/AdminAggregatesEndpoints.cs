using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Queries;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Admin.API.Contracts;
using Purview.EventSourcing.Admin.API.Filters;
using Purview.EventSourcing.Admin.Security;
using ZodSharp.Core;

namespace Purview.EventSourcing.Admin.API.Endpoints;

/// <summary>
/// Maps the Admin portal aggregate query endpoints onto a route group.
/// </summary>
public static class AdminAggregatesEndpoints
{
	const string AdminPortalTag = "AdminPortal";

	// The event export stream follows the same JSON naming as the rest of the Admin API (camelCase).
	static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web);

	/// <summary>
	/// Maps the <c>POST /aggregates/search</c> endpoint used to search for aggregates.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapSearchAggregates(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.SearchAggregates
	)
	{
		return group
			.MapPost("/aggregates/search", SearchAggregatesAsync)
			.WithName("SearchAggregates")
			.WithSummary("Search for aggregates")
			.WithDescription("Search aggregates by type, id, date range, or status flags with pagination support.")
			.WithTags(AdminPortalTag)
			.Produces<PagedResult<AggregateSummaryResponse>>()
			.ProducesValidationProblem()
			.RequireAuthorization(policyName)
			.AddEndpointFilterFactory(
				(routeHandlerContext, next) =>
				{
					var factory = routeHandlerContext.ApplicationServices.GetRequiredService<IZodSchemaFactory>();
					ZodSchemaValidationEndpointFilter<AggregateSearchRequest> filter = new(
						factory,
						request =>
							AdminContractRefinements.InvalidTimeRange(
								request.FromUtc,
								request.ToUtc,
								nameof(AggregateSearchRequest.FromUtc)
							)
					);
					return invocation => filter.InvokeAsync(invocation, next);
				}
			);
	}

	/// <summary>
	/// Maps the <c>GET /aggregates/{aggregateType}/{aggregateId}</c> endpoint used to view a single aggregate.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapViewAggregate(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.ViewAggregate
	)
	{
		return group
			.MapGet("/aggregates/{aggregateType}/{aggregateId}", GetAggregateAsync)
			.WithName("GetAggregate")
			.WithSummary("Get aggregate")
			.WithDescription("Returns a single aggregate summary by type and identifier.")
			.WithTags(AdminPortalTag)
			.Produces<AggregateSummaryResponse>()
			.ProducesProblem(StatusCodes.Status404NotFound)
			.RequireAuthorization(policyName);
	}

	/// <summary>
	/// Maps the <c>GET /aggregates/{aggregateType}/{aggregateId}/events</c> endpoint used to read an aggregate's event stream.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapEventRange(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.ViewEvents
	)
	{
		return group
			.MapGet("/aggregates/{aggregateType}/{aggregateId}/events", GetEventRangeAsync)
			.WithName("GetAggregateEventRange")
			.WithSummary("Get aggregate event range")
			.WithDescription("Returns the aggregate event stream within version and timestamp bounds.")
			.WithTags(AdminPortalTag)
			.Produces<PagedResult<EventEnvelopeResponse>>()
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesValidationProblem()
			.RequireAuthorization(policyName)
			.AddEndpointFilterFactory(
				(routeHandlerContext, next) =>
				{
					var factory = routeHandlerContext.ApplicationServices.GetRequiredService<IZodSchemaFactory>();
					ZodSchemaValidationEndpointFilter<EventRangeRequest> filter = new(factory, RefineEventRangeRequest);
					return invocation => filter.InvokeAsync(invocation, next);
				}
			);
	}

	/// <summary>
	/// Maps the <c>GET /aggregates/{aggregateType}/{aggregateId}/projection</c> endpoint used to project state at a version.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapProjectionAtVersion(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.ProjectPointInTime
	)
	{
		return group
			.MapGet("/aggregates/{aggregateType}/{aggregateId}/projection", GetProjectionAtVersionAsync)
			.WithName("GetAggregateProjectionAtVersion")
			.WithSummary("Get aggregate projection at version")
			.WithDescription("Projects aggregate state at a specific version.")
			.WithTags(AdminPortalTag)
			.Produces<ProjectionResponse>()
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.RequireAuthorization(policyName);
	}

	/// <summary>
	/// Maps the <c>GET /aggregates/{aggregateType}/{aggregateId}/projection/time</c> endpoint used to project state at a timestamp.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapProjectionAtTime(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.ProjectPointInTime
	)
	{
		return group
			.MapGet("/aggregates/{aggregateType}/{aggregateId}/projection/time", GetProjectionAtTimeAsync)
			.WithName("GetAggregateProjectionAtTime")
			.WithSummary("Get aggregate projection at time")
			.WithDescription("Projects aggregate state at a specific UTC timestamp.")
			.WithTags(AdminPortalTag)
			.Produces<ProjectionResponse>()
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.RequireAuthorization(policyName);
	}

	/// <summary>
	/// Maps the <c>GET /aggregates/{aggregateType}/{aggregateId}/events/export</c> endpoint used to export an aggregate's event stream.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapExportEvents(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.ExportEvents
	)
	{
		return group
			.MapGet("/aggregates/{aggregateType}/{aggregateId}/events/export", ExportEventsAsync)
			.WithName("ExportAggregateEvents")
			.WithSummary("Export aggregate events")
			.WithDescription("Streams the aggregate event stream as JSON Lines (application/x-ndjson).")
			.WithTags(AdminPortalTag)
			.Produces<byte[]>(StatusCodes.Status200OK, contentType: "application/x-ndjson")
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesValidationProblem()
			.RequireAuthorization(policyName)
			.AddEndpointFilterFactory(
				(routeHandlerContext, next) =>
				{
					var factory = routeHandlerContext.ApplicationServices.GetRequiredService<IZodSchemaFactory>();
					ZodSchemaValidationEndpointFilter<EventRangeRequest> filter = new(factory, RefineEventRangeRequest);
					return invocation => filter.InvokeAsync(invocation, next);
				}
			);
	}

	static async Task<Ok<PagedResult<AggregateSummaryResponse>>> SearchAggregatesAsync(
		AggregateSearchRequest request,
		IAdminAggregateQueryService queryService,
		IOptions<AdminPortalOptions> options,
		CancellationToken cancellationToken
	)
	{
		var pageSize = ClampPageSize(request.PageSize, options.Value.Paging.MaxPageSize);

		AggregateSearchQuery query = new(
			request.AggregateType,
			request.AggregateId,
			request.FromUtc,
			request.ToUtc,
			request.IsDeleted,
			request.IsRestored,
			Math.Max(request.Page, 1),
			pageSize,
			request.Sort
		);

		var result = await queryService.SearchAsync(query, cancellationToken);
		return TypedResults.Ok(result);
	}

	static async Task<Results<Ok<AggregateSummaryResponse>, NotFound, ValidationProblem>> GetAggregateAsync(
		string aggregateType,
		string aggregateId,
		IAdminAggregateQueryService queryService,
		CancellationToken cancellationToken
	)
	{
		var routeValidation = ValidateRouteParameters(aggregateType, aggregateId);
		if (routeValidation is not null)
			return routeValidation;

		var result = await queryService.GetAsync(aggregateType, aggregateId, cancellationToken);
		return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
	}

	static async Task<Results<Ok<PagedResult<EventEnvelopeResponse>>, NotFound, ValidationProblem>> GetEventRangeAsync(
		string aggregateType,
		string aggregateId,
		[AsParameters] EventRangeRequest request,
		IAdminEventQueryService queryService,
		[FromServices] IAuthorizationService authorizationService,
		HttpContext httpContext,
		IOptions<AdminPortalOptions> options,
		CancellationToken cancellationToken
	)
	{
		var routeValidation = ValidateRouteParameters(aggregateType, aggregateId);
		if (routeValidation is not null)
			return routeValidation;

		EventRangeQuery query = new(
			request.VersionFrom,
			request.VersionTo,
			request.TimeFromUtc,
			request.TimeToUtc,
			Math.Max(request.Page, 1),
			ClampPageSize(request.PageSize, options.Value.Paging.MaxPageSize),
			request.Sort
		);

		var result = await queryService.GetRangeAsync(aggregateType, aggregateId, query, cancellationToken);
		if (result is null)
			return TypedResults.NotFound();

		var payloadAuthorization = await authorizationService.AuthorizeAsync(
			httpContext.User,
			aggregateType,
			AdminPortalPolicies.ViewEventPayloads
		);
		if (payloadAuthorization.Succeeded)
			return TypedResults.Ok(result);

		var redacted = result with
		{
			Items = result.Items.Select(envelope => envelope with { Payload = null }).ToArray(),
		};
		return TypedResults.Ok(redacted);
	}

	static async Task<Results<Ok<ProjectionResponse>, NotFound, ValidationProblem>> GetProjectionAtVersionAsync(
		string aggregateType,
		string aggregateId,
		[FromQuery] long? version,
		IAdminProjectionService projectionService,
		CancellationToken cancellationToken
	)
	{
		var routeValidation = ValidateRouteParameters(aggregateType, aggregateId);
		if (routeValidation is not null)
			return routeValidation;

		if (version is null or <= 0)
			return TypedResults.ValidationProblem(
				new Dictionary<string, string[]> { ["version"] = ["version must be a positive integer."] }
			);

		var result = await projectionService.ProjectAtVersionAsync(
			aggregateType,
			aggregateId,
			version.Value,
			cancellationToken
		);

		return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
	}

	static async Task<Results<Ok<ProjectionResponse>, NotFound, ValidationProblem>> GetProjectionAtTimeAsync(
		string aggregateType,
		string aggregateId,
		[FromQuery] DateTimeOffset? asOfUtc,
		IAdminProjectionService projectionService,
		CancellationToken cancellationToken
	)
	{
		var routeValidation = ValidateRouteParameters(aggregateType, aggregateId);
		if (routeValidation is not null)
			return routeValidation;

		if (asOfUtc is null)
		{
			return TypedResults.ValidationProblem(
				new Dictionary<string, string[]> { ["asOfUtc"] = ["asOfUtc is required."] }
			);
		}

		if (asOfUtc.Value.Offset != TimeSpan.Zero)
		{
			return TypedResults.ValidationProblem(
				new Dictionary<string, string[]> { ["asOfUtc"] = ["asOfUtc must be expressed in UTC (no offset)."] }
			);
		}

		var result = await projectionService.ProjectAtTimeAsync(
			aggregateType,
			aggregateId,
			asOfUtc.Value,
			cancellationToken
		);

		return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
	}

	static async Task<Results<FileStreamHttpResult, NotFound, ValidationProblem>> ExportEventsAsync(
		string aggregateType,
		string aggregateId,
		[AsParameters] EventRangeRequest request,
		IAdminEventQueryService queryService,
		IOptions<AdminPortalOptions> options,
		HttpContext httpContext,
		CancellationToken cancellationToken
	)
	{
		var routeValidation = ValidateRouteParameters(aggregateType, aggregateId);
		if (routeValidation is not null)
			return routeValidation;

		var batchSize = ClampPageSize(request.PageSize, options.Value.Paging.MaxPageSize);
		var remaining = (long)options.Value.Projections.MaxVersionsPerQuery;
		var page = 1;
		var truncated = false;

		var firstPage = await queryService.GetRangeAsync(
			aggregateType,
			aggregateId,
			BuildExportQuery(request, page, batchSize),
			cancellationToken
		);

		if (firstPage is null)
			return TypedResults.NotFound();

		MemoryStream stream = new();
		await using (
			var writer = new StreamWriter(
				stream,
				new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
				bufferSize: 4096,
				leaveOpen: true
			)
		)
		{
			var current = firstPage;
			while (true)
			{
				foreach (var envelope in current.Items)
				{
					if (remaining <= 0)
					{
						// The export cap was reached before all events could be written.
						truncated = true;
						break;
					}

					remaining--;
					await writer.WriteLineAsync(
						JsonSerializer.Serialize(envelope, ExportJsonOptions).AsMemory(),
						cancellationToken
					);
				}

				if (truncated || !current.HasNextPage)
					break;

				page++;
				var next = await queryService.GetRangeAsync(
					aggregateType,
					aggregateId,
					BuildExportQuery(request, page, batchSize),
					cancellationToken
				);
				if (next is null)
					break;

				current = next;
			}

			await writer.FlushAsync(cancellationToken);
		}

		stream.Position = 0;
		httpContext.Response.Headers["Purview-Event-Export-Truncated"] = truncated ? "true" : "false";
		return TypedResults.Stream(stream, "application/x-ndjson");
	}

	static EventRangeQuery BuildExportQuery(EventRangeRequest request, int page, int pageSize) =>
		new(
			request.VersionFrom,
			request.VersionTo,
			request.TimeFromUtc,
			request.TimeToUtc,
			page,
			pageSize,
			"Version asc"
		);

	static int ClampPageSize(int requested, int maxPageSize) => Math.Clamp(requested, 1, Math.Max(maxPageSize, 1));

	static IEnumerable<ValidationError> RefineEventRangeRequest(EventRangeRequest request) =>
		AdminContractRefinements
			.InvalidVersionRange(request.VersionFrom, request.VersionTo, nameof(EventRangeRequest.VersionFrom))
			.Concat(
				AdminContractRefinements.InvalidVersionBound(request.VersionFrom, nameof(EventRangeRequest.VersionFrom))
			)
			.Concat(
				AdminContractRefinements.InvalidVersionBound(request.VersionTo, nameof(EventRangeRequest.VersionTo))
			)
			.Concat(
				AdminContractRefinements.InvalidTimeRange(
					request.TimeFromUtc,
					request.TimeToUtc,
					nameof(EventRangeRequest.TimeFromUtc)
				)
			);

	static ValidationProblem? ValidateRouteParameters(string aggregateType, string aggregateId)
	{
		Dictionary<string, string[]> errors = [];
		if (string.IsNullOrWhiteSpace(aggregateType) || aggregateType.Length > 256)
			errors["aggregateType"] = ["aggregateType must be a non-empty value of at most 256 characters."];

		if (string.IsNullOrWhiteSpace(aggregateId) || aggregateId.Length > 256)
			errors["aggregateId"] = ["aggregateId must be a non-empty value of at most 256 characters."];

		return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
	}
}
