using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.InMemory.Events;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Giving up on an event that will never succeed, without losing it.
/// </summary>
/// <remarks>
/// <para>
/// Delivery is at-least-once, so the checkpoint is never advanced past an event the subscriber
/// rejected. That is correct, and it means a permanently failing event stalls its stream forever -
/// including every event behind it. Dead-lettering is the escape hatch.
/// </para>
/// <para>
/// <strong>The tests that matter most here are the ones that assert nothing is skipped.</strong> An
/// escape hatch that opens too easily is worse than no escape hatch: it turns a visible stall into
/// silent data loss, and nothing downstream would ever know.
/// </para>
/// </remarks>
public sealed class SubscriptionDeadLetterTests
{
	// Asserted on the option itself, because every behavioural test below can be made to pass by a
	// fixture that supplies its own value. This is the one that notices the default changing.
	[Test]
	public async Task MaxDeliveryAttempts_DefaultsToDisabled()
	{
		await Assert.That(new SubscriptionOptions().MaxDeliveryAttempts).IsEqualTo(0);
	}

	// The default. Both safeguards off: a failing event is retried forever and the stream waits.
	[Test]
	public async Task RunCycleAsync_GivenNoConfiguration_DoesNotSkipAFailingEvent()
	{
		await using Fixture fixture = new();
		await fixture.SeedAsync(events: 3);

		var first = await fixture.RunCycleAsync();
		var second = await fixture.RunCycleAsync();

		await Assert.That(first.EventsDeadLettered).IsEqualTo(0);
		await Assert.That(second.EventsDeadLettered).IsEqualTo(0);
		// Still at the first event on the second cycle: nothing was passed over.
		await Assert.That(fixture.Subscriber.VersionsSeen).IsEquivalentTo([1L, 1L]);
	}

	// Enabling the option alone must change nothing, because the default sink accepts nothing.
	// Two deliberate steps are required before any event can be passed over.
	[Test]
	public async Task RunCycleAsync_GivenAttemptsButNoSink_StillDoesNotSkip()
	{
		await using Fixture fixture = new(maxDeliveryAttempts: 1);
		await fixture.SeedAsync(events: 3);

		var result = await fixture.RunCycleAsync();

		await Assert.That(result.EventsDeadLettered).IsEqualTo(0);
		await Assert.That(fixture.Subscriber.VersionsSeen).IsEquivalentTo([1L]);
	}

	// A sink registered but no attempt budget is equally inert.
	[Test]
	public async Task RunCycleAsync_GivenASinkButNoAttemptBudget_StillDoesNotSkip()
	{
		RecordingDeadLetterSink sink = new();
		await using Fixture fixture = new(maxDeliveryAttempts: 0, sink: sink);
		await fixture.SeedAsync(events: 3);

		await fixture.RunCycleAsync();

		await Assert.That(sink.Entries).IsEmpty();
	}

	[Test]
	public async Task RunCycleAsync_GivenBothConfigured_DeadLettersAndContinuesTheStream()
	{
		RecordingDeadLetterSink sink = new();
		await using Fixture fixture = new(maxDeliveryAttempts: 2, sink: sink);
		fixture.Subscriber.FailOnVersion = 2;
		await fixture.SeedAsync(events: 3);

		// First cycle delivers 1, fails 2 once, and stops there - one attempt is below the budget.
		var first = await fixture.RunCycleAsync();
		// Second cycle fails 2 a second time, which reaches the budget, dead-letters it, and goes on.
		var second = await fixture.RunCycleAsync();

		await Assert.That(first.EventsDeadLettered).IsEqualTo(0);
		await Assert.That(second.EventsDeadLettered).IsEqualTo(1);
		await Assert.That(sink.Entries.Single().AggregateVersion).IsEqualTo(2L);
		await Assert.That(second.EventsDelivered).IsEqualTo(1);
		// Version 3 was reached, which is the whole point: the stream behind the stall moved.
		await Assert.That(fixture.Subscriber.VersionsSeen).Contains(3L);
	}

	// The checkpoint, not just the in-cycle counter, has to have moved - otherwise the next cycle
	// redelivers the dead-lettered event and the stream is still stalled.
	[Test]
	public async Task RunCycleAsync_AfterDeadLettering_DoesNotRedeliverTheEvent()
	{
		RecordingDeadLetterSink sink = new();
		await using Fixture fixture = new(maxDeliveryAttempts: 1, sink: sink);
		fixture.Subscriber.FailOnVersion = 2;
		await fixture.SeedAsync(events: 3);

		await fixture.RunCycleAsync();
		fixture.Subscriber.VersionsSeen.Clear();
		await fixture.RunCycleAsync();

		await Assert.That(fixture.Subscriber.VersionsSeen).DoesNotContain(2L);
		await Assert.That(sink.Entries.Count).IsEqualTo(1);
	}

	// A sink that refuses has not recorded the failure, so passing the event over would lose it with
	// nothing anywhere to say it happened.
	[Test]
	public async Task RunCycleAsync_GivenASinkThatRefuses_LeavesTheStreamStalled()
	{
		await using Fixture fixture = new(maxDeliveryAttempts: 1, sink: new RefusingDeadLetterSink());
		fixture.Subscriber.FailOnVersion = 1;
		await fixture.SeedAsync(events: 3);

		await fixture.RunCycleAsync();
		fixture.Subscriber.VersionsSeen.Clear();
		await fixture.RunCycleAsync();

		await Assert.That(fixture.Subscriber.VersionsSeen).IsEquivalentTo([1L]);
	}

	// A sink that throws is the same case as one that refuses, and must not take the cycle down.
	[Test]
	public async Task RunCycleAsync_GivenASinkThatThrows_LeavesTheStreamStalledAndTheCycleIntact()
	{
		await using Fixture fixture = new(maxDeliveryAttempts: 1, sink: new ThrowingDeadLetterSink());
		fixture.Subscriber.FailOnVersion = 1;
		await fixture.SeedAsync(events: 3);

		var result = await fixture.RunCycleAsync();

		await Assert.That(result.StreamsFaulted).IsEqualTo(1);
		await Assert.That(result.EventsDeadLettered).IsEqualTo(0);
	}

	// The dead-letter record identifies the event rather than copying it: the stream is still the
	// canonical record, and a second copy of the payload would outlive crypto-shredding.
	[Test]
	public async Task DeadLetteredEvent_IdentifiesTheEventWithoutCopyingIt()
	{
		RecordingDeadLetterSink sink = new();
		await using Fixture fixture = new(maxDeliveryAttempts: 1, sink: sink);
		fixture.Subscriber.FailOnVersion = 1;
		await fixture.SeedAsync(events: 1, tenant: "acme");

		await fixture.RunCycleAsync(TenantScope.For("acme"));

		var entry = sink.Entries.Single();

		await Assert.That(entry.SubscriptionName).IsEqualTo("failing-subscription");
		await Assert.That(entry.AggregateId).IsEqualTo("stream-1");
		await Assert.That(entry.AggregateVersion).IsEqualTo(1L);
		await Assert.That(entry.Attempts).IsEqualTo(1);
		await Assert.That(entry.Scope.TenantId).IsEqualTo("acme");
		await Assert.That(entry.Error).Contains("the subscriber cannot handle this");
	}

	// Attempts are counted across cycles, not within one: the processor is scoped and rebuilt every
	// cycle, so a counter held inside it would reset and never reach any budget.
	[Test]
	public async Task DeliveryAttempts_AreCountedAcrossCyclesNotWithinOne()
	{
		RecordingDeadLetterSink sink = new();
		await using Fixture fixture = new(maxDeliveryAttempts: 3, sink: sink);
		fixture.Subscriber.FailOnVersion = 1;
		await fixture.SeedAsync(events: 1);

		await fixture.RunCycleAsync();
		await fixture.RunCycleAsync();

		await Assert.That(sink.Entries).IsEmpty();

		await fixture.RunCycleAsync();

		await Assert.That(sink.Entries.Single().Attempts).IsEqualTo(3);
	}

	sealed class Fixture : IAsyncDisposable
	{
		readonly ServiceProvider _provider;
		readonly InMemorySubscriptionCheckpointStore _checkpoints = new();
		readonly ISubscriptionDeadLetterSink _sink;
		readonly ISubscriptionDeliveryAttemptTracker _attempts = new InMemorySubscriptionDeliveryAttemptTracker();
		readonly int? _maxDeliveryAttempts;

		// Nullable, so "no configuration" means the option's own default rather than a zero this
		// fixture supplied. Passing 0 here made the default-behaviour test pass even after the
		// default was changed - it was asserting the fixture, not the framework.
		public Fixture(int? maxDeliveryAttempts = null, ISubscriptionDeadLetterSink? sink = null)
		{
			_maxDeliveryAttempts = maxDeliveryAttempts;
			_sink = sink ?? new NoSubscriptionDeadLetterSink(NullLogger<NoSubscriptionDeadLetterSink>.Instance);

			ServiceCollection services = new();
			services.AddLogging();
			services.AddEventSourcing().AddInMemoryEventStore();
			services.AddSingleton<
				IInMemoryEventStore<SubscriptionTestAggregate>,
				InMemoryEventStore<SubscriptionTestAggregate>
			>();

			_provider = services.BuildServiceProvider();
		}

		public FailingSubscriber Subscriber { get; } = new();

		public async Task SeedAsync(int events, string? tenant = null)
		{
			var store = _provider.GetRequiredService<IInMemoryEventStore<SubscriptionTestAggregate>>();
			var scope = tenant is null ? TenantScope.System : TenantScope.For(tenant);

			using (AmbientCurrentTenant.BeginScope(scope))
			{
				var aggregate = await store.CreateAsync("stream-1");
				for (var index = 0; index < events; index++)
					aggregate.Touch();

				await store.SaveAsync(aggregate, new() { RequiresValidPrincipalIdentifier = false });
			}
		}

		public async Task<SubscriptionCycleResult> RunCycleAsync(TenantScope? scope = null)
		{
			var store = _provider.GetRequiredService<IInMemoryEventStore<SubscriptionTestAggregate>>();

			SubscriptionOptions options = new() { CheckpointInterval = 1 };
			if (_maxDeliveryAttempts is not null)
				options.MaxDeliveryAttempts = _maxDeliveryAttempts.Value;

			EventSubscriptionProcessor<SubscriptionTestAggregate> processor = new(
				Subscriber,
				_checkpoints,
				new EventStoreAggregateStreamDiscovery<SubscriptionTestAggregate>(store),
				store,
				new AmbientCurrentTenant(),
				_sink,
				_attempts,
				Options.Create(options),
				NullLogger<EventSubscriptionProcessor<SubscriptionTestAggregate>>.Instance
			);

			using (AmbientCurrentTenant.BeginScope(scope ?? TenantScope.System))
				return await processor.RunCycleAsync(CancellationToken.None);
		}

		public ValueTask DisposeAsync() => _provider.DisposeAsync();
	}

	sealed class FailingSubscriber : IEventSubscriber<SubscriptionTestAggregate>
	{
		public string SubscriptionName => "failing-subscription";

		/// <summary>The version that always throws. Defaults to the first event.</summary>
		public long FailOnVersion { get; set; } = 1;

		public List<long> VersionsSeen { get; } = [];

		public Task OnEventAsync(
			EventSubscriptionContext<SubscriptionTestAggregate> context,
			CancellationToken cancellationToken
		)
		{
			VersionsSeen.Add(context.Record.Metadata.AggregateVersion);

			return context.Record.Metadata.AggregateVersion == FailOnVersion
				? throw new InvalidOperationException("the subscriber cannot handle this")
				: Task.CompletedTask;
		}
	}

	sealed class RecordingDeadLetterSink : ISubscriptionDeadLetterSink
	{
		public List<DeadLetteredEvent> Entries { get; } = [];

		public Task<bool> DeadLetterAsync(DeadLetteredEvent deadLettered, CancellationToken cancellationToken)
		{
			Entries.Add(deadLettered);

			return Task.FromResult(true);
		}
	}

	sealed class RefusingDeadLetterSink : ISubscriptionDeadLetterSink
	{
		public Task<bool> DeadLetterAsync(DeadLetteredEvent deadLettered, CancellationToken cancellationToken) =>
			Task.FromResult(false);
	}

	sealed class ThrowingDeadLetterSink : ISubscriptionDeadLetterSink
	{
		public Task<bool> DeadLetterAsync(DeadLetteredEvent deadLettered, CancellationToken cancellationToken) =>
			throw new InvalidOperationException("the sink is unavailable");
	}
}
