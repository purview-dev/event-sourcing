using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Queries;
using Purview.EventSourcing.Admin.Security;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Manifest;
using Purview.EventSourcing.Outbox;
using Purview.EventSourcing.Services;

namespace Purview.EventSourcing.Admin.API.Endpoints;

/// <summary>
/// Maps the Admin portal operational endpoints (capability contract and health summary).
/// These are read-only, opt-in, and separately authorized.
/// </summary>
public static class AdminOperationalEndpoints
{
	const string AdminPortalTag = "AdminPortal";

	/// <summary>
	/// Maps the <c>GET /capabilities</c> endpoint that reports the merged event-store capability
	/// contract for the registered providers.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapCapabilities(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.ViewCapabilities
	)
	{
		return group
			.MapGet("/capabilities", GetCapabilitiesAsync)
			.WithName("GetCapabilities")
			.WithSummary("Get event store capabilities")
			.WithDescription("Returns the merged event-store capability contract for the registered providers.")
			.WithTags(AdminPortalTag)
			.Produces<EventStoreCapabilities>()
			.RequireAuthorization(policyName);
	}

	/// <summary>
	/// Maps the <c>GET /health</c> endpoint that reports operational readiness for the Admin portal.
	/// Readiness probes the configured storage when the provider registers an
	/// <see cref="IAdminStorageProbe"/>; without one the status is <c>Unknown</c> and the response
	/// describes only the capability contract.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapHealth(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.ViewCapabilities
	)
	{
		return group
			.MapGet("/health", GetHealthAsync)
			.WithName("GetAdminHealth")
			.WithSummary("Get Admin portal health")
			.WithDescription(
				"Reports whether the configured storage is reachable, and the event-store capability contract."
			)
			.WithTags(AdminPortalTag)
			.Produces<AdminHealthResponse>()
			.RequireAuthorization(policyName);
	}

	static async Task<Ok<EventStoreCapabilities>> GetCapabilitiesAsync(
		IEventStoreCapabilitiesProvider capabilitiesProvider,
		IAdminAuditLogger auditLogger,
		HttpContext httpContext,
		CancellationToken cancellationToken
	)
	{
		var capabilities = capabilitiesProvider.GetCapabilities();

		await auditLogger.LogAsync(
			new AdminAuditEntry(
				DateTimeOffset.UtcNow,
				AdminFeature.ViewCapabilities,
				"Read",
				Principal(httpContext),
				Target: null,
				Succeeded: true,
				Details: null
			),
			cancellationToken
		);

		return TypedResults.Ok(capabilities);
	}

	static async Task<Ok<AdminHealthResponse>> GetHealthAsync(
		IEventStoreCapabilitiesProvider capabilitiesProvider,
		IAdminAuditLogger auditLogger,
		HttpContext httpContext,
		CancellationToken cancellationToken,
		// Explicitly from services: a GET endpoint otherwise infers an unannotated complex parameter
		// as a request body, which minimal APIs reject.
		[FromServices] IAdminStorageProbe? storageProbe = null,
		[FromServices] ILogger<AdminHealthResponse>? logger = null
	)
	{
		var capabilities = capabilitiesProvider.GetCapabilities();
		var status = await ProbeStorageAsync(storageProbe, logger, cancellationToken);

		await auditLogger.LogAsync(
			new AdminAuditEntry(
				DateTimeOffset.UtcNow,
				AdminFeature.ViewCapabilities,
				"Read",
				Principal(httpContext),
				Target: "health",
				Succeeded: true,
				Details: null
			),
			cancellationToken
		);

		return TypedResults.Ok(
			new AdminHealthResponse(
				Status: status,
				TimestampUtc: DateTimeOffset.UtcNow,
				TransactionGuarantee: capabilities.TransactionGuarantee,
				SupportsEventStreams: capabilities.SupportsEventStreams,
				SupportsQueries: capabilities.SupportsQueries,
				SupportsTransactionalOutbox: capabilities.SupportsTransactionalOutbox,
				OperationalLimitations: capabilities.OperationalLimitations
			)
		);
	}

	/// <summary>
	/// Resolves the reported health status, probing storage when the registered provider supports it.
	/// </summary>
	/// <remarks>
	/// The failure is logged rather than returned: a storage error can carry endpoint or credential
	/// detail, and this endpoint answers an unauthenticated-adjacent monitoring caller.
	/// </remarks>
	static async Task<string> ProbeStorageAsync(
		IAdminStorageProbe? storageProbe,
		ILogger? logger,
		CancellationToken cancellationToken
	)
	{
		if (storageProbe is null)
			return AdminHealthStatus.Unknown;

		try
		{
			return await storageProbe.IsReachableAsync(cancellationToken)
				? AdminHealthStatus.Healthy
				: AdminHealthStatus.Unhealthy;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
#pragma warning disable CA1031
		catch (Exception ex)
#pragma warning restore CA1031
		{
			logger?.LogError(ex, "The Admin portal storage probe failed.");

			return AdminHealthStatus.Unhealthy;
		}
	}

	/// <summary>
	/// Maps the <c>GET /outbox/poisoned</c> endpoint that lists poisoned (dead-letter) transactional
	/// outbox messages.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapPoisonedOutbox(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.ViewPoisonedOutbox
	)
	{
		return group
			.MapGet("/outbox/poisoned", GetPoisonedOutboxAsync)
			.WithName("GetPoisonedOutbox")
			.WithSummary("Get poisoned outbox messages")
			.WithDescription("Returns poisoned (dead-letter) transactional outbox messages.")
			.WithTags(AdminPortalTag)
			.Produces<PoisonedOutboxResponse>()
			.RequireAuthorization(policyName);
	}

	static async Task<Results<Ok<PoisonedOutboxResponse>, NotFound>> GetPoisonedOutboxAsync(
		IAdminAuditLogger auditLogger,
		HttpContext httpContext,
		CancellationToken cancellationToken
	)
	{
		var outboxStore = httpContext.RequestServices.GetService<IOutboxStore>();
		if (outboxStore is null)
			return TypedResults.NotFound();

		var messages = await outboxStore.GetPoisonedAsync(0, 100, cancellationToken);

		await auditLogger.LogAsync(
			new AdminAuditEntry(
				DateTimeOffset.UtcNow,
				AdminFeature.ViewPoisonedOutbox,
				"Read",
				Principal(httpContext),
				Target: "outbox/poisoned",
				Succeeded: true,
				Details: null
			),
			cancellationToken
		);

		return TypedResults.Ok(new PoisonedOutboxResponse(messages, messages.Count));
	}

	/// <summary>
	/// Maps the <c>GET /manifest</c> endpoint that reports the runtime event-contract manifest and its
	/// compatibility status.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapManifest(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.ViewManifest
	)
	{
		return group
			.MapGet("/manifest", GetManifestAsync)
			.WithName("GetEventContractManifest")
			.WithSummary("Get event contract manifest")
			.WithDescription("Returns the runtime event-contract manifest and its compatibility status.")
			.WithTags(AdminPortalTag)
			.Produces<EventContractManifestInfo>()
			.RequireAuthorization(policyName);
	}

	static async Task<Results<Ok<EventContractManifestInfo>, NotFound>> GetManifestAsync(
		IAdminAuditLogger auditLogger,
		HttpContext httpContext,
		CancellationToken cancellationToken
	)
	{
		var provider = httpContext.RequestServices.GetService<IEventContractManifestProvider>();
		if (provider is null)
			return TypedResults.NotFound();

		var info = await provider.GetAsync(cancellationToken);

		await auditLogger.LogAsync(
			new AdminAuditEntry(
				DateTimeOffset.UtcNow,
				AdminFeature.ViewManifest,
				"Read",
				Principal(httpContext),
				Target: "manifest",
				Succeeded: true,
				Details: info.CompatibilityStatus.ToString()
			),
			cancellationToken
		);

		return TypedResults.Ok(info);
	}

	/// <summary>
	/// Maps the <c>GET /aggregates/{aggregateType}/{aggregateId}/events/unknown</c> endpoint that
	/// reports stored event type names the runtime cannot resolve to a registered event type.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapUnknownEvents(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.ViewUnknownEvents
	)
	{
		return group
			.MapGet("/aggregates/{aggregateType}/{aggregateId}/events/unknown", GetUnknownEventsAsync)
			.WithName("GetUnknownEvents")
			.WithSummary("Get unknown events in an aggregate stream")
			.WithDescription(
				"Reports stored event type names the runtime cannot resolve to a registered event type — events that would be skipped during replay."
			)
			.WithTags(AdminPortalTag)
			.Produces<UnknownEventsResponse>()
			.ProducesProblem(StatusCodes.Status404NotFound)
			.RequireAuthorization(policyName);
	}

	static async Task<IResult> GetUnknownEventsAsync(
		string aggregateType,
		string aggregateId,
		IAdminEventQueryService queryService,
		IAdminAuditLogger auditLogger,
		HttpContext httpContext,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);

		var typeRegistry = httpContext.RequestServices.GetService<IAggregateTypeRegistry>();
		var eventNameMapper = httpContext.RequestServices.GetService<IAggregateEventNameMapper>();
		if (typeRegistry is null || eventNameMapper is null)
			return TypedResults.NotFound();

		if (!typeRegistry.TryResolve(aggregateType, out _))
			return TypedResults.NotFound();

		HashSet<string> unknown = new(StringComparer.Ordinal);
		var totalEvents = 0;
		var page = 1;
		const int pageSize = 500;

		while (true)
		{
			EventRangeQuery query = new(
				VersionFrom: null,
				VersionTo: null,
				TimeFromUtc: null,
				TimeToUtc: null,
				Page: page,
				PageSize: pageSize,
				Sort: "Version asc"
			);
			var result = await queryService.GetRangeAsync(aggregateType, aggregateId, query, cancellationToken);
			if (result.IsFailure)
				return result.Error.ToHttpResult();

			foreach (var envelope in result.Value.Items)
			{
				totalEvents++;
				var eventType = envelope.Metadata.EventType;
				if (string.IsNullOrEmpty(eventType))
					continue;

				if (eventNameMapper.GetTypeName(eventType) is null)
					unknown.Add(eventType);
			}

			if (!result.Value.HasNextPage)
				break;

			page++;
		}

		await auditLogger.LogAsync(
			new AdminAuditEntry(
				DateTimeOffset.UtcNow,
				AdminFeature.ViewUnknownEvents,
				"Read",
				Principal(httpContext),
				$"{aggregateType}/{aggregateId}",
				Succeeded: true,
				Details: null
			),
			cancellationToken
		);

		return TypedResults.Ok(
			new UnknownEventsResponse(
				aggregateType,
				aggregateId,
				unknown.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
				totalEvents
			)
		);
	}

	static string? Principal(HttpContext httpContext) => httpContext.User.Identity?.Name;
}

/// <summary>
/// A report of stored event type names the runtime cannot resolve to a registered event type.
/// </summary>
/// <param name="AggregateType">The aggregate type name.</param>
/// <param name="AggregateId">The aggregate identifier.</param>
/// <param name="UnknownEventNames">The stored event names with no registered runtime type, ordered ordinally.</param>
/// <param name="TotalEvents">The total number of events inspected in the stream.</param>
public sealed record UnknownEventsResponse(
	string AggregateType,
	string AggregateId,
	IReadOnlyList<string> UnknownEventNames,
	int TotalEvents
);

/// <summary>
/// A page of poisoned (dead-letter) transactional outbox messages.
/// </summary>
/// <param name="Items">The poisoned messages, most recently poisoned first.</param>
/// <param name="Count">The number of messages returned.</param>
public sealed record PoisonedOutboxResponse(IReadOnlyList<OutboxEnvelope> Items, int Count);
