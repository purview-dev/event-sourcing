namespace Purview.EventSourcing.Aggregates;

public partial class AggregateBaseTests
{
	[Test]
	public async Task ForceSave_GivenNoUnsavedEvents_RecordsForceSaveEvent()
	{
		// Arrange
		Test.TestAggregate aggregate = new();

		// Act
		aggregate.ForceSave();

		// Assert
		await Assert.That(aggregate.HasUnsavedEvents()).IsTrue();
		await Assert.That(aggregate.GetUnsavedEvents().Count).IsEqualTo(1);
	}

	[Test]
	public async Task ForceSave_GivenExistingUnsavedEvents_DoesNotRecordForceSaveEvent()
	{
		// Arrange
		Test.TestAggregate aggregate = new();
		aggregate.RecordEvent();

		var eventCountBefore = aggregate.GetUnsavedEvents().Count;

		// Act
		aggregate.ForceSave();

		// Assert — no additional event added since unsaved events already exist
		await Assert.That(aggregate.GetUnsavedEvents().Count).IsEqualTo(eventCountBefore);
	}

	[Test]
	public async Task AggregateType_UsesTypeNameHelper_RemovesAggregateSuffix()
	{
		// Arrange
		Test.TestAggregate aggregate = new();

		// Assert — "TestAggregate" becomes "test" via TypeNameHelper
		await Assert.That(aggregate.AggregateType).IsEqualTo("test");
	}
}
