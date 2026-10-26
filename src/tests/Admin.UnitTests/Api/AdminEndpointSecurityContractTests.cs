using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Purview.EventSourcing.Admin.API;

/// <summary>
/// The security properties every Admin endpoint must have, asserted structurally.
/// </summary>
/// <remarks>
/// <para>
/// The per-endpoint tests check the endpoints that exist today. These check the <em>set</em>: they
/// enumerate what the application actually mapped and assert a property of all of it, so an
/// endpoint added later is covered without anyone remembering to cover it.
/// </para>
/// <para>
/// That distinction matters most on a security boundary. An endpoint that forgets
/// <c>RequireAuthorization</c> is not a failing test, it is an unauthenticated route nobody notices,
/// and the Admin API exposes event payloads and mutating operations.
/// </para>
/// </remarks>
public sealed class AdminEndpointSecurityContractTests
{
	// The route group applies a blanket <c>RequireAuthorization()</c>, so this holds for anything
	// mapped onto it. What it catches is an endpoint mapped outside the group, or one that calls
	// AllowAnonymous - both of which look ordinary in review and are unauthenticated in production.
	[Test]
	public async Task EveryAdminEndpoint_RequiresAuthorization()
	{
		await using var host = await AdminTestHost.CreateAsync();

		var unprotected = AdminEndpoints(host)
			.Where(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>() is null)
			.Select(Describe)
			.ToArray();

		await Assert.That(unprotected).IsEmpty();
	}

	// This is the assertion that bites. The group's blanket authorization is authentication only:
	// any signed-in caller passes it, so an endpoint that forgets its own policy is readable by
	// everyone who can log in. Removing one `.RequireAuthorization(policyName)` fails this test and
	// nothing else, which is exactly the defect it exists for.
	[Test]
	public async Task EveryAdminEndpoint_NamesAnAuthorizationPolicy()
	{
		await using var host = await AdminTestHost.CreateAsync();

		var withoutPolicy = AdminEndpoints(host)
			.Where(endpoint =>
				endpoint
					.Metadata.GetOrderedMetadata<IAuthorizeData>()
					.All(data => string.IsNullOrWhiteSpace(data.Policy))
			)
			.Select(Describe)
			.ToArray();

		await Assert.That(withoutPolicy).IsEmpty();
	}

	// An Admin route mapped outside the configured prefix escapes the group, and with it the group's
	// authorization. It would still be enumerated here - the filter matches on "admin" anywhere in
	// the pattern - so this test sees it where a prefix-scoped search would not.
	[Test]
	public async Task AdminEndpoints_AreAllUnderTheConfiguredPrefix()
	{
		await using var host = await AdminTestHost.CreateAsync();

		var prefix = host.App.Services.GetRequiredService<IOptions<AdminPortalOptions>>().Value.RoutePrefix.Trim('/');

		var outside = AdminEndpoints(host)
			.Where(endpoint =>
				endpoint
					.RoutePattern.RawText?.TrimStart('/')
					.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) != true
			)
			.Select(Describe)
			.ToArray();

		await Assert.That(outside).IsEmpty();
	}

	// Deny-by-default is the whole posture: with a provider that grants nothing, no endpoint may
	// answer with data. Asserted across the set, so a new endpoint cannot opt out of it.
	[Test]
	public async Task EveryAdminEndpoint_IsRefusedWhenThePrincipalIsEntitledToNothing()
	{
		await using var host = await AdminTestHost.CreateAsync(permissionProvider: new DenyAllPermissionProvider());

		List<string> answered = [];
		var probed = 0;
		foreach (var endpoint in AdminEndpoints(host))
		{
			var route = endpoint.RoutePattern.RawText;
			if (route is null || route.Contains('{', StringComparison.Ordinal))
				// Parameterised routes need a concrete id; the per-endpoint tests cover those, and
				// guessing one here would assert against a 404 rather than the authorization result.
				continue;

			probed++;

			var response = await host.Client.GetAsync(new Uri(route, UriKind.Relative));
			if (response.IsSuccessStatusCode)
				answered.Add(route);
		}

		// Without this the test passes when the filter matches nothing, which is how a structural
		// test quietly stops testing anything.
		await Assert.That(probed).IsGreaterThan(0);
		await Assert.That(answered).IsEmpty();
	}

	// Matched on "admin" anywhere in the pattern rather than on the configured prefix, so an Admin
	// endpoint mapped to the wrong place is still enumerated - and then caught by the prefix test.
	static IReadOnlyList<RouteEndpoint> AdminEndpoints(AdminTestHost host) =>
		[
			.. host
				.App.Services.GetRequiredService<EndpointDataSource>()
				.Endpoints.OfType<RouteEndpoint>()
				.Where(endpoint =>
					endpoint.RoutePattern.RawText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true
				),
		];

	static string Describe(Endpoint endpoint) => endpoint.DisplayName ?? endpoint.ToString() ?? "<unnamed>";

	// Entitled to nothing: the posture the Admin API is supposed to start from.
	sealed class DenyAllPermissionProvider : Services.IAdminPermissionProvider
	{
		public Task<IReadOnlyList<Models.AdminPermission>> GetPermissionsAsync(
			System.Security.Claims.ClaimsPrincipal user,
			CancellationToken cancellationToken
		) => Task.FromResult<IReadOnlyList<Models.AdminPermission>>([]);
	}
}
