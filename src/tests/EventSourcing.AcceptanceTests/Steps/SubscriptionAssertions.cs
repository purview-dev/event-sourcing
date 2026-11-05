using BDTest;

namespace Purview.EventSourcing.Steps;

/// <summary>
/// The outcomes a subscription scenario asserts.
/// </summary>
/// <param name="context">The scenario's state.</param>
public sealed class SubscriptionAssertions(SubscriptionScenarioContext context)
{
	[StepText("the subscriber received {count} events")]
	public async Task TheSubscriberReceived(int count) =>
		await Assert.That(context.Subscriber.Delivered.Count).IsEqualTo(count);

	[StepText("the subscriber received nothing")]
	public async Task TheSubscriberReceivedNothing() => await Assert.That(context.Subscriber.Delivered).IsEmpty();

	[StepText("every event the subscriber received belonged to tenant {tenant}")]
	public async Task EveryEventBelongedToTenant(string tenant) =>
		await Assert.That(context.Subscriber.Delivered.All(delivered => delivered.TenantId == tenant)).IsTrue();

	[StepText("the events arrived in version order")]
	public async Task TheEventsArrivedInVersionOrder()
	{
		var versions = context.Subscriber.Delivered.Select(delivered => delivered.AggregateVersion).ToArray();

		await Assert.That(versions).IsEquivalentTo(versions.OrderBy(version => version).ToArray());
	}

	[StepText("no event was delivered more than once")]
	public async Task NoEventWasDeliveredTwice()
	{
		var delivered = context.Subscriber.Delivered.ToArray();

		await Assert.That(delivered.Distinct().Count()).IsEqualTo(delivered.Length);
	}

	[StepText("the cycle reported {count} faulted streams")]
	public async Task TheCycleReportedFaultedStreams(int count) =>
		await Assert.That(context.LastCycle!.Value.StreamsFaulted).IsEqualTo(count);
}
