using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Contracts;

/// <summary>
/// Provider-agnostic contract for idempotency marker retention.
/// </summary>
/// <remarks>
/// <para>
/// Linked into each provider's integration test project in the same way as the event-store contract
/// suite, so a provider cannot implement <see cref="IIdempotencyMarkerMaintenance"/> without
/// answering the same questions: does it honour the retention window, does it leave recent markers
/// alone, and does it stay inside the tenant it is running as.
/// </para>
/// <para>
/// Everything is asserted through the returned count rather than by inspecting storage. Markers have
/// no public contract - they are an implementation detail of idempotent save - so a test that
/// counted rows would have to know each provider's layout, and would then be testing the test.
/// </para>
/// <para>
/// The tenancy case matters more than it looks. Cleanup is a bulk delete, so a missing tenant filter
/// does not leak another tenant's data - it destroys it, which is worse and just as silent.
/// </para>
/// </remarks>
/// <typeparam name="TAggregate">The aggregate type under test.</typeparam>
public abstract class EventStoreIdempotencyMarkerRetentionTestsBase<TAggregate>
	where TAggregate : class, IAggregateTest, new()
{
	/// <summary>
	/// Creates a store scoped to <paramref name="scope"/>, sharing storage with every other store
	/// built from the same <paramref name="runId"/>.
	/// </summary>
	/// <remarks>
	/// Abstract, and sharing storage is the point: two stores over different tables would pass the
	/// isolation test while proving nothing.
	/// </remarks>
	protected abstract IEventStoreCore<TAggregate> CreateTenantScopedEventStore(TenantScope scope, Guid runId);

	[Test]
	public async Task CleanupIdempotencyMarkersAsync_GivenOnlyRecentMarkers_RemovesNone(
		CancellationToken cancellationToken
	)
	{
		var eventStore = CreateTenantScopedEventStore(TenantScope.System, Guid.NewGuid());
		await SaveAsync(eventStore, cancellationToken);

		var removed = await Maintenance(eventStore)
			.CleanupIdempotencyMarkersAsync(TimeSpan.FromDays(1), cancellationToken);

		await Assert.That(removed).IsEqualTo(0);
	}

	[Test]
	public async Task CleanupIdempotencyMarkersAsync_GivenExpiredMarkers_RemovesThem(
		CancellationToken cancellationToken
	)
	{
		var eventStore = CreateTenantScopedEventStore(TenantScope.System, Guid.NewGuid());
		await SaveAsync(eventStore, cancellationToken);

		// A one-millisecond window makes every marker already written older than the cutoff, without
		// the test having to wait for one.
		var removed = await Maintenance(eventStore)
			.CleanupIdempotencyMarkersAsync(TimeSpan.FromMilliseconds(1), cancellationToken);

		await Assert.That(removed).IsGreaterThan(0);
	}

	[Test]
	public async Task CleanupIdempotencyMarkersAsync_RunTwice_HasNothingLeftToRemove(
		CancellationToken cancellationToken
	)
	{
		var eventStore = CreateTenantScopedEventStore(TenantScope.System, Guid.NewGuid());
		await SaveAsync(eventStore, cancellationToken);

		await Maintenance(eventStore).CleanupIdempotencyMarkersAsync(TimeSpan.FromMilliseconds(1), cancellationToken);
		var second = await Maintenance(eventStore)
			.CleanupIdempotencyMarkersAsync(TimeSpan.FromMilliseconds(1), cancellationToken);

		await Assert.That(second).IsEqualTo(0);
	}

	// A bulk delete that forgets the tenant destroys another tenant's data rather than exposing it.
	// Cleaning as one tenant must leave the other's markers for the other to clean.
	[Test]
	public async Task CleanupIdempotencyMarkersAsync_RunAsOneTenant_LeavesAnotherTenantsMarkers(
		CancellationToken cancellationToken
	)
	{
		var runId = Guid.NewGuid();
		var acme = CreateTenantScopedEventStore(TenantScope.For("acme"), runId);
		var globex = CreateTenantScopedEventStore(TenantScope.For("globex"), runId);

		await SaveAsync(acme, cancellationToken);
		await SaveAsync(globex, cancellationToken);

		var acmeRemoved = await Maintenance(acme)
			.CleanupIdempotencyMarkersAsync(TimeSpan.FromMilliseconds(1), cancellationToken);
		var globexRemoved = await Maintenance(globex)
			.CleanupIdempotencyMarkersAsync(TimeSpan.FromMilliseconds(1), cancellationToken);

		await Assert.That(acmeRemoved).IsGreaterThan(0);
		await Assert.That(globexRemoved).IsGreaterThan(0);
	}

	[Test]
	public async Task CleanupIdempotencyMarkersAsync_GivenANonPositiveRetention_Throws(
		CancellationToken cancellationToken
	)
	{
		var eventStore = CreateTenantScopedEventStore(TenantScope.System, Guid.NewGuid());

		await Assert
			.That(async () =>
				await Maintenance(eventStore).CleanupIdempotencyMarkersAsync(TimeSpan.Zero, cancellationToken)
			)
			.Throws<ArgumentOutOfRangeException>();
	}

	static IIdempotencyMarkerMaintenance Maintenance(IEventStoreCore<TAggregate> eventStore) =>
		(IIdempotencyMarkerMaintenance)eventStore;

	// Markers are opt-in - EventStoreOperationContext.UseIdempotencyMarkerDefault is false - so a
	// plain save writes none and there would be nothing to clean up.
	static async Task SaveAsync(IEventStoreCore<TAggregate> eventStore, CancellationToken cancellationToken)
	{
		var aggregate = await eventStore.CreateAsync($"{Guid.NewGuid()}", cancellationToken);
		aggregate.AppendString("marker");

		await eventStore.SaveAsync(
			aggregate,
			new EventStoreOperationContext { UseIdempotencyMarker = true },
			cancellationToken
		);
	}
}
