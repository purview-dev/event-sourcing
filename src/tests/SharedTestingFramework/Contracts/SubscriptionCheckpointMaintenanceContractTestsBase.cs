using Purview.EventSourcing.Subscriptions;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing;

/// <summary>
/// The behaviours every <see cref="ISubscriptionCheckpointMaintenance"/> implementation must share.
/// </summary>
/// <remarks>
/// <para>
/// Nothing removed checkpoint rows before this, so a retired subscription left one behind for every
/// stream it had ever read. Removing them is easy; removing <em>only</em> them is the part worth
/// testing, because a delete that catches one row too many silently replays a production stream.
/// </para>
/// <para>
/// <strong>Every test uses one store and distinguishes rows only by key.</strong> Each provider
/// expresses the key differently - two columns, a compound <c>_id</c>, a partition key - and a delete
/// that is right in one of those shapes is easy to get wrong in another.
/// </para>
/// </remarks>
public abstract class SubscriptionCheckpointMaintenanceContractTestsBase
{
	/// <summary>Creates a store addressing storage shared with every other store this test makes.</summary>
	protected abstract ISubscriptionCheckpointStore CreateCheckpointStore();

	static ISubscriptionCheckpointMaintenance Maintenance(ISubscriptionCheckpointStore store) =>
		store as ISubscriptionCheckpointMaintenance
		?? throw new InvalidOperationException(
			$"{store.GetType().Name} does not implement ISubscriptionCheckpointMaintenance, so retired subscriptions would leave rows nothing removes."
		);

	[Test]
	public async Task RemoveSubscriptionAsync_RemovesEveryCheckpointItHolds(CancellationToken cancellationToken)
	{
		var store = CreateCheckpointStore();
		var subscription = $"retired-{Guid.NewGuid():N}";
		var scope = TenantScope.For("acme");

		SubscriptionCheckpointKey first = new(subscription, scope, $"{Guid.NewGuid():N}");
		SubscriptionCheckpointKey second = new(subscription, scope, $"{Guid.NewGuid():N}");

		await store.SetAsync(first, 10, cancellationToken);
		await store.SetAsync(second, 20, cancellationToken);

		var removed = await Maintenance(store).RemoveSubscriptionAsync(subscription, scope, cancellationToken);

		await Assert.That(removed).IsEqualTo(2);
		await Assert.That(await store.GetAsync(first, cancellationToken)).IsNull();
		await Assert.That(await store.GetAsync(second, cancellationToken)).IsNull();

		Dispose(store);
	}

	// The one that matters. A delete keyed on the subscription name alone removes every tenant's
	// position, and each of them replays its entire stream on the next cycle.
	[Test]
	public async Task RemoveSubscriptionAsync_LeavesAnotherTenantsCheckpointsAlone(CancellationToken cancellationToken)
	{
		var store = CreateCheckpointStore();
		var subscription = $"retired-{Guid.NewGuid():N}";
		var aggregateId = $"{Guid.NewGuid():N}";

		SubscriptionCheckpointKey acme = new(subscription, TenantScope.For("acme"), aggregateId);
		SubscriptionCheckpointKey globex = new(subscription, TenantScope.For("globex"), aggregateId);

		await store.SetAsync(acme, 10, cancellationToken);
		await store.SetAsync(globex, 20, cancellationToken);

		// Asserted before the delete so a failure says which half broke. Without it, a write that
		// never landed is indistinguishable from a delete that took one row too many, and the two
		// call for completely different fixes.
		await Assert.That(await store.GetAsync(globex, cancellationToken)).IsEqualTo(20);

		await Maintenance(store).RemoveSubscriptionAsync(subscription, TenantScope.For("acme"), cancellationToken);

		await Assert.That(await store.GetAsync(acme, cancellationToken)).IsNull();
		await Assert.That(await store.GetAsync(globex, cancellationToken)).IsEqualTo(20);

		Dispose(store);
	}

	// System is a scope like any other here, not a wildcard. If it removed every tenant's rows, an
	// operator retiring an infrastructure subscription would replay all of them.
	[Test]
	public async Task RemoveSubscriptionAsync_GivenTheSystemScope_LeavesTenantCheckpointsAlone(
		CancellationToken cancellationToken
	)
	{
		var store = CreateCheckpointStore();
		var subscription = $"retired-{Guid.NewGuid():N}";
		var aggregateId = $"{Guid.NewGuid():N}";

		SubscriptionCheckpointKey system = new(subscription, TenantScope.System, aggregateId);
		SubscriptionCheckpointKey acme = new(subscription, TenantScope.For("acme"), aggregateId);

		await store.SetAsync(system, 10, cancellationToken);
		await store.SetAsync(acme, 20, cancellationToken);

		// See the note on the tenant test above: this separates "the write never landed" from "the
		// delete over-matched".
		await Assert.That(await store.GetAsync(acme, cancellationToken)).IsEqualTo(20);

		await Maintenance(store).RemoveSubscriptionAsync(subscription, TenantScope.System, cancellationToken);

		await Assert.That(await store.GetAsync(system, cancellationToken)).IsNull();
		await Assert.That(await store.GetAsync(acme, cancellationToken)).IsEqualTo(20);

		Dispose(store);
	}

	[Test]
	public async Task RemoveSubscriptionAsync_LeavesAnotherSubscriptionsCheckpointsAlone(
		CancellationToken cancellationToken
	)
	{
		var store = CreateCheckpointStore();
		var scope = TenantScope.For("acme");
		var aggregateId = $"{Guid.NewGuid():N}";
		var retired = $"retired-{Guid.NewGuid():N}";
		var keeping = $"keeping-{Guid.NewGuid():N}";

		SubscriptionCheckpointKey first = new(retired, scope, aggregateId);
		SubscriptionCheckpointKey second = new(keeping, scope, aggregateId);

		await store.SetAsync(first, 10, cancellationToken);
		await store.SetAsync(second, 20, cancellationToken);

		await Maintenance(store).RemoveSubscriptionAsync(retired, scope, cancellationToken);

		await Assert.That(await store.GetAsync(second, cancellationToken)).IsEqualTo(20);

		Dispose(store);
	}

	[Test]
	public async Task RemoveSubscriptionAsync_GivenNothingToRemove_ReportsZero(CancellationToken cancellationToken)
	{
		var store = CreateCheckpointStore();

		var removed = await Maintenance(store)
			.RemoveSubscriptionAsync($"never-ran-{Guid.NewGuid():N}", TenantScope.For("acme"), cancellationToken);

		await Assert.That(removed).IsEqualTo(0);

		Dispose(store);
	}

	[Test]
	public async Task RemoveCheckpointAsync_RemovesOnlyThatStreamsPosition(CancellationToken cancellationToken)
	{
		var store = CreateCheckpointStore();
		var subscription = $"projection-{Guid.NewGuid():N}";
		var scope = TenantScope.For("acme");

		SubscriptionCheckpointKey pruned = new(subscription, scope, $"{Guid.NewGuid():N}");
		SubscriptionCheckpointKey kept = new(subscription, scope, $"{Guid.NewGuid():N}");

		await store.SetAsync(pruned, 10, cancellationToken);
		await store.SetAsync(kept, 20, cancellationToken);

		var removed = await Maintenance(store).RemoveCheckpointAsync(pruned, cancellationToken);

		await Assert.That(removed).IsTrue();
		await Assert.That(await store.GetAsync(pruned, cancellationToken)).IsNull();
		await Assert.That(await store.GetAsync(kept, cancellationToken)).IsEqualTo(20);

		Dispose(store);
	}

	[Test]
	public async Task RemoveCheckpointAsync_GivenNoCheckpoint_ReportsThatNothingWasRemoved(
		CancellationToken cancellationToken
	)
	{
		var store = CreateCheckpointStore();

		var removed = await Maintenance(store)
			.RemoveCheckpointAsync(
				new($"projection-{Guid.NewGuid():N}", TenantScope.For("acme"), $"{Guid.NewGuid():N}"),
				cancellationToken
			);

		await Assert.That(removed).IsFalse();

		Dispose(store);
	}

	static void Dispose(ISubscriptionCheckpointStore store)
	{
		if (store is IDisposable disposable)
			disposable.Dispose();
	}
}
