using System.Security.Claims;
using Purview.EventSourcing.Admin.Failures;
using Purview.EventSourcing.Admin.Security.Providers;

namespace Purview.EventSourcing.Admin.Security;

/// <summary>
/// Guards the rule that an admin request is always scoped to exactly one named tenant.
/// </summary>
public sealed class ClaimsAdminTenantResolverTests
{
	static ClaimsPrincipal UserWith(params string[] tenants) =>
		new(new ClaimsIdentity([.. tenants.Select(t => new Claim(ClaimsAdminTenantResolver.DefaultClaimType, t))]));

	static Task<Purview.Results.Result<string, AdminTenantFailure>> ResolveAsync(
		ClaimsPrincipal user,
		string? requested = null
	) => new ClaimsAdminTenantResolver().ResolveAsync(user, requested, CancellationToken.None);

	[Test]
	public async Task ResolveAsync_GivenASingleEntitlement_ScopesToIt()
	{
		var result = await ResolveAsync(UserWith("acme"));

		await Assert.That(result.IsSuccess).IsTrue();
		await Assert.That(result.Value).IsEqualTo("acme");
	}

	// The super user case: several entitlements and no choice made. Defaulting to the first would
	// silently pick a tenant, and falling back to the system scope would hand a browser session a
	// cross-tenant view.
	[Test]
	public async Task ResolveAsync_GivenSeveralEntitlementsAndNoSelection_IsDenied()
	{
		var result = await ResolveAsync(UserWith("acme", "globex"));

		await Assert.That(result.IsFailure).IsTrue();
		await Assert.That(result.Error is AdminTenantNotSelected).IsTrue();
	}

	[Test]
	public async Task ResolveAsync_GivenSeveralEntitlementsAndASelection_ScopesToTheSelection()
	{
		var result = await ResolveAsync(UserWith("acme", "globex"), requested: "globex");

		await Assert.That(result.IsSuccess).IsTrue();
		await Assert.That(result.Value).IsEqualTo("globex");
	}

	[Test]
	public async Task ResolveAsync_GivenAnUnentitledSelection_IsForbidden()
	{
		var result = await ResolveAsync(UserWith("acme"), requested: "globex");

		await Assert.That(result.IsFailure).IsTrue();
		await Assert.That(result.Error is AdminTenantForbidden).IsTrue();
	}

	[Test]
	public async Task ResolveAsync_GivenNoEntitlements_IsDenied()
	{
		var result = await ResolveAsync(UserWith());

		await Assert.That(result.IsFailure).IsTrue();
		await Assert.That(result.Error is AdminTenantNotSelected).IsTrue();
	}
}
