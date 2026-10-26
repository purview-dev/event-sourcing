using BDTest;
using Purview.EventSourcing.Steps;

namespace Purview.EventSourcing;

/// <summary>
/// How the store reports the outcome of a save, stated as scenarios.
/// </summary>
/// <remarks>
/// The distinction these protect is that a save which wrote nothing is a success, while a lost update
/// is a failure - the ambiguity the previous bool-returning API could not express.
/// </remarks>
public sealed class SaveOutcomeScenarios : TUnitBDTestBase<EventStoreScenarioContext>
{
	EventStoreSteps Steps => field ??= new(Context);

	EventStoreAssertions Expect => field ??= new(Context);

	[Test]
	public async Task SavingWithNothingToWriteIsASuccess() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 5))
			.When(() => Steps.TheAggregateIsSavedWithNoChanges())
			.Then(() => Expect.TheSaveSucceedsHavingWrittenNothing())
			.WithScenarioText("Saving an aggregate with nothing to write is a success, not a failure");

	[Test]
	public async Task ALostUpdateIsReportedRatherThanThrown() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 5))
			.When(() => Steps.ASecondWriterSavesACompetingChange())
			.Then(() => Expect.TheSaveIsRefusedAsAConcurrencyConflict())
			.WithScenarioText("A lost update is reported as a concurrency conflict, not thrown");
}
