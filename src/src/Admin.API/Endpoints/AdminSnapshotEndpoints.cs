using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Admin.Security;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.Services;
using System.Linq.Expressions;
using System.Reflection;

namespace Purview.EventSourcing.Admin.API.Endpoints;

/// <summary>
/// Maps the Admin portal snapshot endpoints: read-only snapshot status and an authorized, audited,
/// idempotent snapshot rebuild. Rebuild reconstructs the aggregate from its canonical event stream
/// and persists a fresh snapshot through the queryable store.
/// </summary>
public static class AdminSnapshotEndpoints
{
	const string AdminPortalTag = "AdminPortal";

	/// <summary>
	/// Maps the <c>GET /aggregates/{aggregateType}/{aggregateId}/snapshot</c> endpoint that reports
	/// whether a snapshot is materialized for the aggregate.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapSnapshotStatus(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.ViewSnapshot
	)
	{
		return group
			.MapGet("/aggregates/{aggregateType}/{aggregateId}/snapshot", GetSnapshotStatusAsync)
			.WithName("GetSnapshotStatus")
			.WithSummary("Get aggregate snapshot status")
			.WithDescription(
				"Reports whether a snapshot is materialized for the aggregate and its stored and declared schema versions."
			)
			.WithTags(AdminPortalTag)
			.Produces<SnapshotStatusResponse>()
			.ProducesProblem(StatusCodes.Status404NotFound)
			.RequireAuthorization(policyName);
	}

	/// <summary>
	/// Maps the <c>POST /aggregates/{aggregateType}/{aggregateId}/snapshot/rebuild</c> endpoint that
	/// reconstructs the aggregate from its canonical event stream and persists a fresh snapshot.
	/// </summary>
	/// <param name="group">The route group to map the endpoint onto.</param>
	/// <param name="policyName">The host authorization policy required by the endpoint.</param>
	public static RouteHandlerBuilder MapRebuildSnapshot(
		RouteGroupBuilder group,
		string policyName = AdminPortalPolicies.RebuildSnapshot
	)
	{
		return group
			.MapPost("/aggregates/{aggregateType}/{aggregateId}/snapshot/rebuild", RebuildSnapshotAsync)
			.WithName("RebuildSnapshot")
			.WithSummary("Rebuild an aggregate snapshot")
			.WithDescription(
				"Reconstructs the aggregate from its canonical event stream and persists a fresh snapshot. The operation is idempotent."
			)
			.WithTags(AdminPortalTag)
			.Produces<SnapshotRebuildResponse>()
			.ProducesProblem(StatusCodes.Status404NotFound)
			.RequireAuthorization(policyName);
	}

	static async Task<Results<Ok<SnapshotStatusResponse>, NotFound>> GetSnapshotStatusAsync(
		string aggregateType,
		string aggregateId,
		IAdminAuditLogger auditLogger,
		HttpContext httpContext,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);

		var queryableStore = httpContext.RequestServices.GetService<IQueryableEventStore>();
		var typeRegistry = httpContext.RequestServices.GetService<IAggregateTypeRegistry>();
		if (
			queryableStore is null
			|| typeRegistry is null
			|| !typeRegistry.TryResolve(aggregateType, out var aggregateTypeSymbol)
		)
			return TypedResults.NotFound();

		// FirstOrDefaultAsync queries the snapshot store only, so a non-null result proves a snapshot is materialized.
		var whereClause = BuildIdEqualsExpression(aggregateTypeSymbol, aggregateId);
		var firstOrDefault = GenericInterfaceMethod(
			typeof(IQueryableEventStore),
			"FirstOrDefaultAsync",
			parameterCount: 3
		);
		var snapshot = await InvokeGenericAsync(
			firstOrDefault,
			aggregateTypeSymbol,
			queryableStore,
			[whereClause, null, cancellationToken]
		);

		var exists = snapshot is not null;
		var currentVersion = snapshot is IAggregate value ? value.Details.CurrentVersion : 0;
		var schemaVersion = AggregateSnapshotSchema.GetVersion(aggregateTypeSymbol);

		await auditLogger.LogAsync(
			new AdminAuditEntry(
				DateTimeOffset.UtcNow,
				AdminFeature.ViewSnapshot,
				"Read",
				Principal(httpContext),
				$"{aggregateType}/{aggregateId}",
				Succeeded: true,
				Details: null
			),
			cancellationToken
		);

		return TypedResults.Ok(
			new SnapshotStatusResponse(aggregateType, aggregateId, exists, currentVersion, schemaVersion)
		);
	}

	static async Task<Results<Ok<SnapshotRebuildResponse>, NotFound>> RebuildSnapshotAsync(
		string aggregateType,
		string aggregateId,
		IAdminAuditLogger auditLogger,
		HttpContext httpContext,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);

		var eventStore = httpContext.RequestServices.GetService<IEventStore>();
		var queryableStore = httpContext.RequestServices.GetService<IQueryableEventStore>();
		var typeRegistry = httpContext.RequestServices.GetService<IAggregateTypeRegistry>();
		if (
			eventStore is null
			|| queryableStore is null
			|| typeRegistry is null
			|| !typeRegistry.TryResolve(aggregateType, out var aggregateTypeSymbol)
		)
			return TypedResults.NotFound();

		// Load from the canonical event stream, then persist a fresh snapshot through the queryable store.
		var getAsync = GenericInterfaceMethod(typeof(IEventStore), "GetAsync", parameterCount: 3);
		var aggregate = await InvokeGenericAsync(
			getAsync,
			aggregateTypeSymbol,
			eventStore,
			[aggregateId, null, cancellationToken]
		);
		if (aggregate is null)
			return TypedResults.NotFound();

		var saveAsync = GenericInterfaceMethod(typeof(IEventStore), "SaveAsync", parameterCount: 3);
		var saveResult = await InvokeGenericAsync(
			saveAsync,
			aggregateTypeSymbol,
			queryableStore,
			[aggregate, null, cancellationToken]
		);
		var rebuilt =
			saveResult is not null && (bool)(saveResult.GetType().GetProperty("Saved")?.GetValue(saveResult) ?? false);
		var currentVersion = aggregate is IAggregate value ? value.Details.CurrentVersion : 0;

		await auditLogger.LogAsync(
			new AdminAuditEntry(
				DateTimeOffset.UtcNow,
				AdminFeature.RebuildSnapshot,
				"Rebuild",
				Principal(httpContext),
				$"{aggregateType}/{aggregateId}",
				Succeeded: rebuilt,
				Details: null
			),
			cancellationToken
		);

		return TypedResults.Ok(new SnapshotRebuildResponse(aggregateType, aggregateId, rebuilt, currentVersion));
	}

	static MethodInfo GenericInterfaceMethod(Type interfaceType, string name, int parameterCount) =>
		interfaceType
			.GetMethods()
			.Single(method =>
				method.Name == name
				&& method.IsGenericMethodDefinition
				&& method.GetParameters().Length == parameterCount
			);

	static async Task<object?> InvokeGenericAsync(MethodInfo method, Type typeArgument, object instance, object?[] args)
	{
		var generic = method.MakeGenericMethod(typeArgument);
		var task = (Task)generic.Invoke(instance, args)!;
		await task.ConfigureAwait(false);
		return task.GetType().GetProperty("Result")?.GetValue(task);
	}

	static LambdaExpression BuildIdEqualsExpression(Type aggregateType, string aggregateId)
	{
		var parameter = Expression.Parameter(aggregateType, "x");
		var details = Expression.Property(parameter, nameof(IAggregate.Details));
		var id = Expression.Property(details, nameof(AggregateDetails.Id));
		var body = Expression.Equal(id, Expression.Constant(aggregateId));
		return Expression.Lambda(body, parameter);
	}

	static string? Principal(HttpContext httpContext) => httpContext.User.Identity?.Name;
}

/// <summary>
/// The snapshot status of an aggregate stream.
/// </summary>
/// <param name="AggregateType">The aggregate type name.</param>
/// <param name="AggregateId">The aggregate identifier.</param>
/// <param name="Exists">Whether a snapshot is materialized in the snapshot store.</param>
/// <param name="CurrentVersion">The stored aggregate version, when a snapshot exists.</param>
/// <param name="SchemaVersion">The declared snapshot schema version.</param>
public sealed record SnapshotStatusResponse(
	string AggregateType,
	string AggregateId,
	bool Exists,
	int CurrentVersion,
	int SchemaVersion
);

/// <summary>
/// The outcome of a snapshot rebuild.
/// </summary>
/// <param name="AggregateType">The aggregate type name.</param>
/// <param name="AggregateId">The aggregate identifier.</param>
/// <param name="Rebuilt">Whether a fresh snapshot was persisted.</param>
/// <param name="CurrentVersion">The aggregate version at the time of the rebuild.</param>
public sealed record SnapshotRebuildResponse(
	string AggregateType,
	string AggregateId,
	bool Rebuilt,
	int CurrentVersion
);
