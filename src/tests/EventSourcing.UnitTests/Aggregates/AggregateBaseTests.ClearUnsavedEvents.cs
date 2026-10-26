namespace Purview.EventSourcing.Aggregates;

partial class AggregateBaseTests
{
	[Test]
	[Arguments(10, 10)]
	[Arguments(10, 20)]
	[Arguments(10, 0)]
	[Arguments(10, 100)]
	[Arguments(0, 1)]
	[Arguments(0, 10)]
	[Arguments(0, 100)]
	public async Task ClearUnsavedEvents_GivenNoUpperBoundVersionIsSpecifiedAndHasEvents_ReturnsToPreUnSavedAndAppliedEventVersion(
		int savedEventCount,
		int unSavedEventCount
	)
	{
		// Arrange
		var testAggregate = CreateTestAggregate();
		testAggregate.Details.SavedVersion = testAggregate.Details.CurrentVersion = savedEventCount;

		for (var i = 0; i < unSavedEventCount; i++)
			testAggregate.Increment();

		await Assert.That(testAggregate.Details.CurrentVersion).IsEqualTo(savedEventCount + unSavedEventCount);

		// Act
		testAggregate.ClearUnsavedEvents(upToVersion: null);

		// Assert
		await Assert.That(testAggregate.Details.CurrentVersion).IsEqualTo(savedEventCount);
		await Assert.That(testAggregate.HasUnsavedEvents()).IsFalse();
	}

	[Test]
	[Arguments(100, 10)]
	[Arguments(21, 20)]
	[Arguments(10, 1)]
	[Arguments(10, 9)]
	[Arguments(2, 1)]
	[Arguments(11, 10)]
	[Arguments(111, 100)]
	public async Task ClearUnsavedEvents_GivenUpperBoundVersionIsSpecifiedAndIsLessThanEventsUnsaved_ReturnsToPreUnSavedUpToSpecifiedBound(
		int unSavedEventCount,
		int eventsToRemove
	)
	{
		// Arrange
		var testAggregate = CreateTestAggregate();

		for (var i = 0; i < unSavedEventCount; i++)
			testAggregate.Increment();

		// Act
		testAggregate.ClearUnsavedEvents(upToVersion: eventsToRemove);

		// Assert
		await Assert.That(testAggregate.Details.CurrentVersion).IsEqualTo(unSavedEventCount - eventsToRemove);
	}
}
