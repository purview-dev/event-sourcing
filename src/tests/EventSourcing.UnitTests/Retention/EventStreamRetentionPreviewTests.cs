using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.InMemory.Events;
using Purview.EventSourcing.Subscriptions;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Retention;

/// <summary>
/// The retention preview: what a policy would remove, and what is holding the rest back.
/// </summary>
/// <remarks>
/// The preview exists so a policy can be found to be wrong before it costs anything, so the
/// behaviour worth protecting is that it refuses in every case where pruning would be unsafe -
/// no covering snapshot, events too recent, or a subscription that could never catch up.
/// </remarks>
public sealed class EventStreamRetentionPreviewTests
{
	[Test]
	public async Task PreviewAsync_GivenNoSnapshot_ReportsTheStreamAsBlocked()
	{
		await using Fixture fixture = new();
		await fixture.SeedAsync("aggregate-1", events: 5);

		var preview = await fixture.PreviewAsync(new EventRetentionPolicy { RetainedVersions = 0 });

		await Assert.That(preview.StreamsExamined).IsEqualTo(1);
		await Assert.That(preview.Eligible).IsEmpty();
		await Assert.That(preview.Blockers.Single().Reason).IsEqualTo(RetentionBlockerReason.NoCoveringSnapshot);
	}

	// RequireSnapshot is the guard that stops retention destroying state irrecoverably, so the
	// preview must distinguish "no snapshot" from every other reason.
	[Test]
	public async Task PreviewAsync_WithoutRequiringASnapshot_StillRespectsTheAgeFloor()
	{
		await using Fixture fixture = new();
		await fixture.SeedAsync("aggregate-1", events: 5);

		var preview = await fixture.PreviewAsync(
			new EventRetentionPolicy { RequireSnapshot = false, RetainedVersions = 0 }
		);

		await Assert.That(preview.Eligible).IsEmpty();
		await Assert.That(preview.Blockers.Single().Reason).IsEqualTo(RetentionBlockerReason.TooRecent);
	}

	[Test]
	public async Task PreviewAsync_GivenAShortStream_ReportsNothingToRemove()
	{
		await using Fixture fixture = new();
		await fixture.SeedAsync("aggregate-1", events: 2);

		var preview = await fixture.PreviewAsync(
			new EventRetentionPolicy { RequireSnapshot = false, RetainedVersions = 100 }
		);

		await Assert.That(preview.Blockers.Single().Reason).IsEqualTo(RetentionBlockerReason.NothingToRemove);
	}

	// Pruning past a subscription's checkpoint turns a merely slow projection into a permanently
	// incomplete one, with no error. It has to block.
	[Test]
	public async Task PreviewAsync_GivenASubscriptionBehindThePrunePoint_ReportsItAsBlocked()
	{
		await using Fixture fixture = new(withSubscriber: true);
		await fixture.SeedAsync("aggregate-1", events: 5);

		var preview = await fixture.PreviewAsync(
			new EventRetentionPolicy
			{
				RequireSnapshot = false,
				RetainedVersions = 0,
				MinimumAge = TimeSpan.FromMilliseconds(1),
			}
		);

		var blocker = preview.Blockers.Single();

		await Assert.That(blocker.Reason).IsEqualTo(RetentionBlockerReason.SubscriptionBehind);
		await Assert.That(blocker.Detail).IsEqualTo(Fixture.SubscriptionName);
	}

	[Test]
	public async Task PreviewAsync_WhenTheSubscriptionHasCaughtUp_ReportsTheStreamAsEligible()
	{
		await using Fixture fixture = new(withSubscriber: true);
		await fixture.SeedAsync("aggregate-1", events: 5);
		await fixture.AdvanceCheckpointAsync("aggregate-1", version: 5);

		var preview = await fixture.PreviewAsync(
			new EventRetentionPolicy
			{
				RequireSnapshot = false,
				RetainedVersions = 0,
				MinimumAge = TimeSpan.FromMilliseconds(1),
			}
		);

		await Assert.That(preview.Blockers).IsEmpty();
		await Assert.That(preview.Eligible.Single().AggregateId).IsEqualTo("aggregate-1");
		await Assert.That(preview.EventsRemovable).IsGreaterThan(0);
	}

	// The margin is the difference between an unreadable snapshot being an inconvenience and being
	// data loss, so it must reduce the prune point rather than be advisory.
	[Test]
	public async Task PreviewAsync_RetainedVersions_ReducesThePrunePoint()
	{
		await using Fixture fixture = new();
		await fixture.SeedAsync("aggregate-1", events: 10);

		EventRetentionPolicy policy = new() { RequireSnapshot = false, MinimumAge = TimeSpan.FromMilliseconds(1) };

		var none = await fixture.PreviewAsync(policy with { RetainedVersions = 0 });
		var margin = await fixture.PreviewAsync(policy with { RetainedVersions = 4 });

		await Assert
			.That(margin.Eligible.Single().PruneThroughVersion)
			.IsLessThan(none.Eligible.Single().PruneThroughVersion);
	}

	[Test]
	public async Task PreviewAsync_GivenAnInvalidPolicy_Throws()
	{
		await using Fixture fixture = new();

		await Assert
			.That(async () => await fixture.PreviewAsync(new EventRetentionPolicy { MinimumAge = TimeSpan.Zero }))
			.Throws<InvalidOperationException>();
	}

	sealed class Fixture : IAsyncDisposable
	{
		public const string SubscriptionName = "projection";

		readonly ServiceProvider _provider;

		public Fixture(bool withSubscriber = false)
		{
			ServiceCollection services = new();
			services.AddLogging();
			services.AddEventSourcing().AddInMemoryEventStore();

			// The in-memory store keeps data per instance and is registered transient, so the preview
			// and the seeding must resolve the same one.
			services.AddSingleton<
				IInMemoryEventStore<RetentionTestAggregate>,
				InMemoryEventStore<RetentionTestAggregate>
			>();
			services.AddSingleton<IEventStoreCore<RetentionTestAggregate>>(static provider =>
				provider.GetRequiredService<IInMemoryEventStore<RetentionTestAggregate>>()
			);

			if (withSubscriber)
			{
				services.AddSingleton<IEventSubscriber<RetentionTestAggregate>>(new StubSubscriber());
				services.AddSingleton<ISubscriptionCheckpointStore, InMemorySubscriptionCheckpointStore>();
			}

			services.AddEventStreamRetentionPreview<RetentionTestAggregate>();

			_provider = services.BuildServiceProvider();
		}

		public async Task SeedAsync(string aggregateId, int events)
		{
			var store = _provider.GetRequiredService<IInMemoryEventStore<RetentionTestAggregate>>();
			var aggregate = await store.CreateAsync(aggregateId);
			for (var i = 0; i < events; i++)
				aggregate.Touch();

			await store.SaveAsync(aggregate, new() { RequiresValidPrincipalIdentifier = false });
		}

		public Task AdvanceCheckpointAsync(string aggregateId, long version) =>
			_provider
				.GetRequiredService<ISubscriptionCheckpointStore>()
				.SetAsync(new(SubscriptionName, TenantScope.System, aggregateId), version, CancellationToken.None);

		public Task<RetentionPreview> PreviewAsync(EventRetentionPolicy policy) =>
			_provider
				.GetRequiredService<IEventStreamRetentionPreview<RetentionTestAggregate>>()
				.PreviewAsync(policy, CancellationToken.None);

		public ValueTask DisposeAsync() => _provider.DisposeAsync();

		sealed class StubSubscriber : IEventSubscriber<RetentionTestAggregate>
		{
			public string SubscriptionName => Fixture.SubscriptionName;

			public Task OnEventAsync(
				EventSubscriptionContext<RetentionTestAggregate> context,
				CancellationToken cancellationToken
			) => Task.CompletedTask;
		}
	}
}
