using BDTest;

namespace Purview.EventSourcing.Steps;

/// <summary>
/// The actions a subscription scenario performs.
/// </summary>
/// <param name="context">The scenario's state.</param>
public sealed class SubscriptionSteps(SubscriptionScenarioContext context)
{
	[StepText("tenant {tenant} has an aggregate that was incremented {count} times")]
	public Task AnAggregateExistsForTenant(string tenant, int count) =>
		SubscriptionScenarioContext.AsTenantAsync(
			tenant,
			async () =>
			{
				var aggregate = await context.Store.CreateAsync(context.AggregateId);
				for (var i = 0; i < count; i++)
					aggregate.Increment(1);

				await context.Store.SaveAsync(aggregate, SubscriptionScenarioContext.OperationContext());
			}
		);

	[StepText("tenant {tenant} increments that aggregate {count} more times")]
	public Task TheAggregateIsIncrementedAgain(string tenant, int count) =>
		SubscriptionScenarioContext.AsTenantAsync(
			tenant,
			async () =>
			{
				var read = await context.Store.GetAsync(
					context.AggregateId,
					SubscriptionScenarioContext.OperationContext()
				);

				var aggregate = read.Value;
				for (var i = 0; i < count; i++)
					aggregate.Increment(1);

				await context.Store.SaveAsync(aggregate, SubscriptionScenarioContext.OperationContext());
			}
		);

	[StepText("a subscription runs for tenant {tenant}")]
	public Task TheSubscriptionRunsForTenant(string tenant) => context.RunCycleAsync(tenant);

	[StepText("a subscription runs as infrastructure, across every tenant")]
	public Task TheSubscriptionRunsAsInfrastructure() => context.RunCycleAsync(null);

	[StepText("the subscription for tenant {tenant} runs a second time")]
	public Task TheSubscriptionRunsAgainForTenant(string tenant) => context.RunCycleAsync(tenant);

	[StepText("the subscriber refuses the event at version {version}")]
	public Task TheSubscriberRefusesVersion(int version)
	{
		context.Subscriber.FailOn = delivered => delivered.AggregateVersion == version;

		return Task.CompletedTask;
	}

	[StepText("the subscriber starts accepting every event again")]
	public Task TheSubscriberAcceptsEverything()
	{
		context.Subscriber.FailOn = null;

		return Task.CompletedTask;
	}
}
