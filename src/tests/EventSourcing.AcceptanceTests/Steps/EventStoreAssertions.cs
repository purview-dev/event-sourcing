using BDTest;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;

namespace Purview.EventSourcing.Steps;

/// <summary>
/// The outcomes an acceptance scenario asserts.
/// </summary>
/// <param name="context">The scenario's state.</param>
public sealed class EventStoreAssertions(EventStoreScenarioContext context)
{
	[StepText("the aggregate is returned")]
	public async Task TheAggregateIsReturned() => await Assert.That(context.ReadResult!.Value.IsSuccess).IsTrue();

	[StepText("the aggregate is reported as absent")]
	public async Task TheAggregateIsReportedAsAbsent()
	{
		await Assert.That(context.ReadResult!.Value.IsFailure).IsTrue();
		await Assert.That(context.ReadResult!.Value.Error is AggregateNotFound).IsTrue();
	}

	[StepText("the value is {amount}")]
	public async Task TheValueIs(int amount) =>
		await Assert.That(context.ReadResult!.Value.Value.Value).IsEqualTo(amount);

	[StepText("the save succeeds having written nothing")]
	public async Task TheSaveSucceedsHavingWrittenNothing()
	{
		await Assert.That(context.SaveResult!.Value.IsSuccess).IsTrue();
		await Assert.That(context.SaveResult!.Value.Value.Kind).IsEqualTo(SaveOutcomeKind.NoChanges);
	}

	[StepText("the save is refused as a concurrency conflict")]
	public async Task TheSaveIsRefusedAsAConcurrencyConflict()
	{
		await Assert.That(context.SaveResult!.Value.IsFailure).IsTrue();
		await Assert.That(context.SaveResult!.Value.Error is ConcurrencyConflict).IsTrue();
	}
}
