using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Checkpoint maintenance on the in-memory store.
/// </summary>
/// <remarks>
/// The provider suites run the shared contract against real storage; this runs the same rules with
/// no infrastructure, so a change that breaks the scope matching fails on every pull request rather
/// than only when someone runs the integration suites.
/// </remarks>
public sealed class InMemorySubscriptionCheckpointMaintenanceTests
{
	[Test]
	public async Task RemoveSubscriptionAsync_RemovesEveryCheckpointItHolds()
	{
		InMemorySubscriptionCheckpointStore store = new();
		var scope = TenantScope.For("acme");

		SubscriptionCheckpointKey first = new("retired", scope, "a");
		SubscriptionCheckpointKey second = new("retired", scope, "b");

		await store.SetAsync(first, 10, CancellationToken.None);
		await store.SetAsync(second, 20, CancellationToken.None);

		var removed = await store.RemoveSubscriptionAsync("retired", scope, CancellationToken.None);

		await Assert.That(removed).IsEqualTo(2);
		await Assert.That(await store.GetAsync(first, CancellationToken.None)).IsNull();
	}

	// A delete keyed on the subscription name alone removes every tenant's position, and each of
	// them then replays its whole stream.
	[Test]
	public async Task RemoveSubscriptionAsync_LeavesAnotherTenantsCheckpointsAlone()
	{
		InMemorySubscriptionCheckpointStore store = new();

		SubscriptionCheckpointKey acme = new("retired", TenantScope.For("acme"), "shared-id");
		SubscriptionCheckpointKey globex = new("retired", TenantScope.For("globex"), "shared-id");

		await store.SetAsync(acme, 10, CancellationToken.None);
		await store.SetAsync(globex, 20, CancellationToken.None);

		await store.RemoveSubscriptionAsync("retired", TenantScope.For("acme"), CancellationToken.None);

		await Assert.That(await store.GetAsync(acme, CancellationToken.None)).IsNull();
		await Assert.That(await store.GetAsync(globex, CancellationToken.None)).IsEqualTo(20);
	}

	// System is a scope, not a wildcard: retiring an infrastructure subscription must not replay
	// every tenant's streams.
	[Test]
	public async Task RemoveSubscriptionAsync_GivenTheSystemScope_LeavesTenantCheckpointsAlone()
	{
		InMemorySubscriptionCheckpointStore store = new();

		SubscriptionCheckpointKey system = new("retired", TenantScope.System, "shared-id");
		SubscriptionCheckpointKey acme = new("retired", TenantScope.For("acme"), "shared-id");

		await store.SetAsync(system, 10, CancellationToken.None);
		await store.SetAsync(acme, 20, CancellationToken.None);

		await store.RemoveSubscriptionAsync("retired", TenantScope.System, CancellationToken.None);

		await Assert.That(await store.GetAsync(system, CancellationToken.None)).IsNull();
		await Assert.That(await store.GetAsync(acme, CancellationToken.None)).IsEqualTo(20);
	}

	[Test]
	public async Task RemoveSubscriptionAsync_LeavesAnotherSubscriptionsCheckpointsAlone()
	{
		InMemorySubscriptionCheckpointStore store = new();
		var scope = TenantScope.For("acme");

		SubscriptionCheckpointKey retired = new("retired", scope, "shared-id");
		SubscriptionCheckpointKey keeping = new("keeping", scope, "shared-id");

		await store.SetAsync(retired, 10, CancellationToken.None);
		await store.SetAsync(keeping, 20, CancellationToken.None);

		await store.RemoveSubscriptionAsync("retired", scope, CancellationToken.None);

		await Assert.That(await store.GetAsync(keeping, CancellationToken.None)).IsEqualTo(20);
	}

	[Test]
	public async Task RemoveCheckpointAsync_RemovesOnlyThatStreamsPosition()
	{
		InMemorySubscriptionCheckpointStore store = new();
		var scope = TenantScope.For("acme");

		SubscriptionCheckpointKey pruned = new("projection", scope, "pruned");
		SubscriptionCheckpointKey kept = new("projection", scope, "kept");

		await store.SetAsync(pruned, 10, CancellationToken.None);
		await store.SetAsync(kept, 20, CancellationToken.None);

		var removed = await store.RemoveCheckpointAsync(pruned, CancellationToken.None);

		await Assert.That(removed).IsTrue();
		await Assert.That(await store.GetAsync(kept, CancellationToken.None)).IsEqualTo(20);
	}

	[Test]
	public async Task RemoveCheckpointAsync_GivenNoCheckpoint_ReportsThatNothingWasRemoved()
	{
		InMemorySubscriptionCheckpointStore store = new();

		var removed = await store.RemoveCheckpointAsync(
			new("projection", TenantScope.For("acme"), "absent"),
			CancellationToken.None
		);

		await Assert.That(removed).IsFalse();
	}
}
