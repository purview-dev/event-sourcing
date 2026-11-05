using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Admin.Failures;
using Purview.EventSourcing.Admin.Services;
using Purview.Results;

namespace Purview.EventSourcing.Admin.API;

/// <summary>
/// Guards that tenant scoping is enforced for the whole admin group, and only when asked for.
/// </summary>
public sealed class AdminTenantScopeFilterTests
{
	sealed class StubResolver(Result<string, AdminTenantFailure> result) : IAdminTenantResolver
	{
		public string? ObservedRequestedTenant { get; private set; }

		public Task<Result<string, AdminTenantFailure>> ResolveAsync(
			ClaimsPrincipal user,
			string? requestedTenantId,
			CancellationToken cancellationToken
		)
		{
			ObservedRequestedTenant = requestedTenantId;
			return Task.FromResult(result);
		}
	}

	static async Task<(HttpStatusCode Status, StubResolver Resolver)> CallHealthAsync(
		Result<string, AdminTenantFailure> resolution,
		bool enforce,
		string? tenantHeader,
		CancellationToken cancellationToken
	)
	{
		StubResolver resolver = new(resolution);

		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: options =>
			{
				options.Features.ViewCapabilities = true;
				options.EnforceTenantScope = enforce;
			},
			configureServices: services => services.AddSingleton<IAdminTenantResolver>(resolver)
		);

		if (tenantHeader is not null)
			host.Client.DefaultRequestHeaders.Add("Purview-Tenant", tenantHeader);

		var response = await host.Client.GetAsync("/admin/api/health", cancellationToken);

		return (response.StatusCode, resolver);
	}

	// The default: a single-tenant deployment must be unaffected, and must not be asked for a tenant.
	[Test]
	public async Task Health_WhenTenantScopeNotEnforced_SucceedsWithoutATenant(CancellationToken cancellationToken)
	{
		var (status, resolver) = await CallHealthAsync(
			Result<string, AdminTenantFailure>.Success("acme"),
			enforce: false,
			tenantHeader: null,
			cancellationToken
		);

		await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(resolver.ObservedRequestedTenant).IsNull();
	}

	[Test]
	public async Task Health_WhenTenantResolves_Succeeds(CancellationToken cancellationToken)
	{
		var (status, resolver) = await CallHealthAsync(
			Result<string, AdminTenantFailure>.Success("globex"),
			enforce: true,
			tenantHeader: "globex",
			cancellationToken
		);

		await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(resolver.ObservedRequestedTenant).IsEqualTo("globex");
	}

	[Test]
	public async Task Health_WhenNoTenantSelected_IsRefused(CancellationToken cancellationToken)
	{
		var (status, _) = await CallHealthAsync(
			AdminTenantFailure.Failure<string>(new AdminTenantNotSelected("admin-1")),
			enforce: true,
			tenantHeader: null,
			cancellationToken
		);

		await Assert.That(status).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task Health_WhenTenantNotEntitled_IsForbidden(CancellationToken cancellationToken)
	{
		var (status, _) = await CallHealthAsync(
			AdminTenantFailure.Failure<string>(new AdminTenantForbidden("admin-1", "globex")),
			enforce: true,
			tenantHeader: "globex",
			cancellationToken
		);

		await Assert.That(status).IsEqualTo(HttpStatusCode.Forbidden);
	}
}
