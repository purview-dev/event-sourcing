using BDTest;

namespace Purview.EventSourcing.Steps;

/// <summary>
/// The actions an acceptance scenario performs against the event store.
/// </summary>
/// <remarks>
/// Reusable across scenarios: a step describes an intent in domain language, so the same step reads
/// correctly wherever it appears and a new scenario is assembled rather than written.
/// </remarks>
/// <param name="context">The scenario's state.</param>
public sealed class EventStoreSteps(EventStoreScenarioContext context)
{
	[StepText("tenant {tenant} has an aggregate with a value of {amount}")]
	public Task AnAggregateExistsForTenant(string tenant, int amount) =>
		EventStoreScenarioContext.AsTenantAsync(
			tenant,
			async () =>
			{
				var aggregate = await context.Store.CreateAsync(context.AggregateId);
				aggregate.Increment(amount);

				await context.Store.SaveAsync(aggregate, EventStoreScenarioContext.OperationContext());
			}
		);

	[StepText("an aggregate exists that belongs to no tenant")]
	public Task AnUnownedAggregateExists() => AnAggregateExistsForTenant(tenant: null!, amount: 1);

	[StepText("tenant {tenant} reads that aggregate")]
	public Task TheTenantReadsTheAggregate(string tenant) =>
		EventStoreScenarioContext.AsTenantAsync(
			tenant,
			async () =>
				context.ReadResult = await context.Store.GetAsync(
					context.AggregateId,
					EventStoreScenarioContext.OperationContext()
				)
		);

	[StepText("infrastructure reads that aggregate across every tenant")]
	public Task InfrastructureReadsTheAggregate() => TheTenantReadsTheAggregate(tenant: null!);

	[StepText("tenant {tenant} reads the aggregate twice, so it is cached")]
	public async Task TheTenantReadsTheAggregateTwice(string tenant)
	{
		await TheTenantReadsTheAggregate(tenant);
		await TheTenantReadsTheAggregate(tenant);
	}

	[StepText("the aggregate is saved again with nothing changed")]
	public Task TheAggregateIsSavedWithNoChanges() =>
		EventStoreScenarioContext.AsTenantAsync(
			null,
			async () =>
			{
				var read = await context.Store.GetAsync(
					context.AggregateId,
					EventStoreScenarioContext.OperationContext()
				);

				context.SaveResult = await context.Store.SaveAsync(
					read.Value,
					EventStoreScenarioContext.OperationContext()
				);
			}
		);

	// A second writer that never saw the first writer's event. This is the shape of a lost update, and
	// the store must report it rather than overwrite or throw.
	[StepText("a second writer saves a competing change to the same aggregate")]
	public Task ASecondWriterSavesACompetingChange() =>
		EventStoreScenarioContext.AsTenantAsync(
			null,
			async () =>
			{
				AcceptanceAggregate stale = new();
				stale.Details.Id = context.AggregateId;
				stale.Increment(1);

				context.SaveResult = await context.Store.SaveAsync(stale, EventStoreScenarioContext.OperationContext());
			}
		);
}
