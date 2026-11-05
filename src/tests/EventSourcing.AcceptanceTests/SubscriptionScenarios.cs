using BDTest;
using Purview.EventSourcing.Steps;

namespace Purview.EventSourcing;

/// <summary>
/// What a subscription promises, stated as scenarios.
/// </summary>
/// <remarks>
/// Tenant isolation comes first, because a subscription reads streams it did not write and a
/// discovery that ignores the tenant hands one tenant's data to another regardless of how the
/// subsequent read is filtered.
/// </remarks>
public sealed class SubscriptionScenarios : TUnitBDTestBase<SubscriptionScenarioContext>
{
	SubscriptionSteps Steps => field ??= new(Context);

	SubscriptionAssertions Expect => field ??= new(Context);

	[Test]
	public async Task ASubscriptionDoesNotReceiveAnotherTenantsEvents() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 3))
			.When(() => Steps.TheSubscriptionRunsForTenant("globex"))
			.Then(() => Expect.TheSubscriberReceivedNothing())
			.WithScenarioText("A subscription does not receive another tenant's events");

	[Test]
	public async Task ASubscriptionReceivesItsOwnTenantsEvents() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 3))
			.When(() => Steps.TheSubscriptionRunsForTenant("acme"))
			.Then(() => Expect.TheSubscriberReceived(3))
			.And(() => Expect.EveryEventBelongedToTenant("acme"))
			.WithScenarioText("A subscription receives its own tenant's events");

	// The position is keyed by tenant, so one tenant's progress must not advance another's past
	// events it has never seen.
	[Test]
	public async Task EachTenantKeepsItsOwnPosition() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 3))
			.And(() => Steps.TheSubscriptionRunsForTenant("acme"))
			.When(() => Steps.TheSubscriptionRunsAsInfrastructure())
			.Then(() => Expect.TheSubscriberReceived(6))
			.WithScenarioText("Each tenant keeps its own subscription position");

	// The whole reason a subscription exists rather than the change feed or the outbox: those only
	// carry what happened while they were running.
	[Test]
	public async Task ASubscriptionReadsEventsWrittenBeforeItRan() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 5))
			.When(() => Steps.TheSubscriptionRunsForTenant("acme"))
			.Then(() => Expect.TheSubscriberReceived(5))
			.And(() => Expect.TheEventsArrivedInVersionOrder())
			.WithScenarioText("A subscription reads events written before it ever ran");

	[Test]
	public async Task ASubscriptionDoesNotRedeliverWhatItAlreadyRead() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 3))
			.And(() => Steps.TheSubscriptionRunsForTenant("acme"))
			.When(() => Steps.TheSubscriptionRunsAgainForTenant("acme"))
			.Then(() => Expect.TheSubscriberReceived(3))
			.And(() => Expect.NoEventWasDeliveredTwice())
			.WithScenarioText("A subscription does not redeliver what it already read");

	[Test]
	public async Task ASubscriptionResumesFromItsPosition() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 3))
			.And(() => Steps.TheSubscriptionRunsForTenant("acme"))
			.And(() => Steps.TheAggregateIsIncrementedAgain("acme", 2))
			.When(() => Steps.TheSubscriptionRunsAgainForTenant("acme"))
			.Then(() => Expect.TheSubscriberReceived(5))
			.And(() => Expect.NoEventWasDeliveredTwice())
			.WithScenarioText("A subscription resumes from its position and reads only what is new");

	// Fail-closed: a refused event stops that stream where it is, rather than being skipped. The
	// alternative loses data silently.
	[Test]
	public async Task ARefusedEventStopsTheStreamRatherThanBeingSkipped() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 4))
			.And(() => Steps.TheSubscriberRefusesVersion(3))
			.When(() => Steps.TheSubscriptionRunsForTenant("acme"))
			.Then(() => Expect.TheSubscriberReceived(2))
			.And(() => Expect.TheCycleReportedFaultedStreams(1))
			.WithScenarioText("A refused event stops that stream rather than being skipped");

	[Test]
	public async Task AStreamResumesOnceTheSubscriberAcceptsTheEventAgain() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 4))
			.And(() => Steps.TheSubscriberRefusesVersion(3))
			.And(() => Steps.TheSubscriptionRunsForTenant("acme"))
			.And(() => Steps.TheSubscriberAcceptsEverything())
			.When(() => Steps.TheSubscriptionRunsAgainForTenant("acme"))
			.Then(() => Expect.TheSubscriberReceived(4))
			.And(() => Expect.NoEventWasDeliveredTwice())
			.WithScenarioText("A stream resumes from the refused event once the subscriber accepts it");
}
