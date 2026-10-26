using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.InMemory.Events;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Tenant isolation for subscriptions, tested at the seam that can leak rather than through the
/// subscription as a whole.
/// </summary>
/// <remarks>
/// A scenario that asserts "the other tenant's subscriber received nothing" passes even when
/// discovery leaks, because the subsequent event read is filtered too and returns nothing. That
/// second filter masks the first. These tests therefore assert on what discovery itself returns:
/// aggregate ids are data, and handing one tenant's ids to another is a leak whether or not the
/// events behind them can be read.
/// </remarks>
public sealed class SubscriptionTenancyTests
{
	[Test]
	public async Task GetAggregateIdsAsync_GivenAnotherTenantsAggregate_DoesNotReturnIt()
	{
		await using Fixture fixture = new();
		await fixture.SeedAsync("acme", "acme-1");

		var discovered = await fixture.DiscoverAsync(TenantScope.For("globex"));

		await Assert.That(discovered).IsEmpty();
	}

	[Test]
	public async Task GetAggregateIdsAsync_GivenItsOwnAggregate_ReturnsIt()
	{
		await using Fixture fixture = new();
		await fixture.SeedAsync("acme", "acme-1");

		var discovered = await fixture.DiscoverAsync(TenantScope.For("acme"));

		await Assert.That(discovered).IsEquivalentTo(["acme-1"]);
	}

	[Test]
	public async Task GetAggregateIdsAsync_GivenSystemScope_ReturnsEveryTenantsAggregates()
	{
		await using Fixture fixture = new();
		await fixture.SeedAsync("acme", "acme-1");
		await fixture.SeedAsync("globex", "globex-1");

		var discovered = await fixture.DiscoverAsync(TenantScope.System);

		await Assert.That(discovered.OrderBy(id => id, StringComparer.Ordinal)).IsEquivalentTo(["acme-1", "globex-1"]);
	}

	// Fail closed: a stream whose write path forgot the tenant stamp must not become visible to
	// every tenant. Only the system scope sees it.
	[Test]
	public async Task GetAggregateIdsAsync_GivenAnUnownedAggregate_IsNotVisibleToATenant()
	{
		await using Fixture fixture = new();
		await fixture.SeedAsync(tenant: null, "unowned-1");

		var discovered = await fixture.DiscoverAsync(TenantScope.For("acme"));

		await Assert.That(discovered).IsEmpty();
	}

	// Two tenants sharing a position would let whichever ran first advance the checkpoint past the
	// other's unread events, and those events would never be delivered.
	[Test]
	public async Task Checkpoints_GivenTheSameSubscriptionAndAggregate_AreSeparatePerTenant()
	{
		InMemorySubscriptionCheckpointStore checkpoints = new();

		SubscriptionCheckpointKey acme = new("projection", TenantScope.For("acme"), "shared-id");
		SubscriptionCheckpointKey globex = new("projection", TenantScope.For("globex"), "shared-id");

		await checkpoints.SetAsync(acme, 50, CancellationToken.None);

		await Assert.That(await checkpoints.GetAsync(acme, CancellationToken.None)).IsEqualTo(50);
		await Assert.That(await checkpoints.GetAsync(globex, CancellationToken.None)).IsNull();
	}

	[Test]
	public async Task Checkpoints_GivenTheSystemScope_IsSeparateFromEveryTenant()
	{
		InMemorySubscriptionCheckpointStore checkpoints = new();

		SubscriptionCheckpointKey system = new("projection", TenantScope.System, "shared-id");
		SubscriptionCheckpointKey acme = new("projection", TenantScope.For("acme"), "shared-id");

		await checkpoints.SetAsync(system, 50, CancellationToken.None);

		await Assert.That(await checkpoints.GetAsync(acme, CancellationToken.None)).IsNull();
	}

	sealed class Fixture : IAsyncDisposable
	{
		readonly ServiceProvider _provider;

		public Fixture()
		{
			ServiceCollection services = new();
			services.AddLogging();
			services.AddEventSourcing().AddInMemoryEventStore();

			// The in-memory store holds its data in the instance and is registered transient, so the
			// seed and the discovery must resolve the same one.
			services.AddSingleton<
				IInMemoryEventStore<SubscriptionTestAggregate>,
				InMemoryEventStore<SubscriptionTestAggregate>
			>();

			_provider = services.BuildServiceProvider();
		}

		public async Task SeedAsync(string? tenant, string aggregateId)
		{
			var store = _provider.GetRequiredService<IInMemoryEventStore<SubscriptionTestAggregate>>();
			var scope = tenant is null ? TenantScope.System : TenantScope.For(tenant);

			using (AmbientCurrentTenant.BeginScope(scope))
			{
				var aggregate = await store.CreateAsync(aggregateId);
				aggregate.Touch();

				await store.SaveAsync(aggregate, new() { RequiresValidPrincipalIdentifier = false });
			}
		}

		public async Task<IReadOnlyList<string>> DiscoverAsync(TenantScope scope)
		{
			var store = _provider.GetRequiredService<IInMemoryEventStore<SubscriptionTestAggregate>>();
			EventStoreAggregateStreamDiscovery<SubscriptionTestAggregate> discovery = new(store);

			using (AmbientCurrentTenant.BeginScope(scope))
			{
				List<string> ids = [];
				await foreach (var id in discovery.GetAggregateIdsAsync(CancellationToken.None))
					ids.Add(id);

				return ids;
			}
		}

		public ValueTask DisposeAsync() => _provider.DisposeAsync();
	}
}
