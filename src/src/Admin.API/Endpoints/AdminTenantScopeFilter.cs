using Microsoft.AspNetCore.Http;
using Purview.EventSourcing.Admin.Failures;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Admin.API.Endpoints;

/// <summary>
/// Resolves the tenant an admin request acts for, and applies it for the duration of the request.
/// </summary>
/// <remarks>
/// Applied to the whole admin group rather than to individual endpoints: an endpoint added later must not
/// be able to escape tenant scoping by omitting a filter. A request that cannot be resolved to exactly one
/// tenant is refused - there is no fallback that serves every tenant.
/// </remarks>
/// <param name="resolver">Decides the tenant from the caller's entitlements and the request.</param>
/// <param name="headerName">The request header naming the tenant to act for.</param>
public sealed class AdminTenantScopeFilter(IAdminTenantResolver resolver, string headerName) : IEndpointFilter
{
	/// <inheritdoc/>
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(next);

		var httpContext = context.HttpContext;
		var requested = httpContext.Request.Headers.TryGetValue(headerName, out var header) ? header.ToString() : null;

		var resolved = await resolver.ResolveAsync(
			httpContext.User,
			string.IsNullOrWhiteSpace(requested) ? null : requested,
			httpContext.RequestAborted
		);

		if (resolved.IsFailure)
		{
			return resolved.Error switch
			{
				AdminTenantForbidden => TypedResults.Problem(
					statusCode: StatusCodes.Status403Forbidden,
					title: "The requested tenant is not available to this user."
				),
				_ => TypedResults.Problem(
					statusCode: StatusCodes.Status400BadRequest,
					title: $"A tenant must be named using the '{headerName}' header."
				),
			};
		}

		using var scope = AmbientCurrentTenant.BeginScope(TenantScope.For(resolved.Value));

		return await next(context);
	}
}
