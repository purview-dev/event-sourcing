using Purview.EventSourcing.Aggregates.Test;
using Purview.EventSourcing.Aggregates.Test.Events;

namespace Purview.EventSourcing.Aggregates;

public partial class AggregateBaseTests
{
	[Test]
	public async Task GetHashCode_GivenIdentificationEvents_GeneratesIdenticalHashCodes()
	{
		// Arrange
		static AppendToReadOnlyDictionaryEvent Generate() =>
			new() { Key = "a-key", Values = ["a-value", "another-value"] };

		var event1 = Generate();
		var event2 = Generate();

		// Act
		var event1HashCode = event1.GetHashCode();
		var event2HashCode = event2.GetHashCode();

		// Assert
		await Assert.That(event1HashCode).IsEqualTo(event2HashCode);
	}

	[Test]
	public async Task RecordEvent_GivenEvent_AppliesEvent()
	{
		// Arrange
		var aggregate = CreateTestAggregate();

		// Act
		aggregate.RecordEvent();

		// Assert
		await Assert.That(aggregate.EventRecorded).IsTrue();
	}

	[Test]
	public async Task GetUnsavedEvents_GivenEvent_RecordsOneEventToBeRecorded()
	{
		// Arrange
		var aggregate = CreateTestAggregate();

		aggregate.RecordEvent();

		// Act
		var events = aggregate.GetUnsavedEvents();

		// Assert
		await Assert.That(events).HasSingleItem();
	}

	[Test]
	[Arguments(10, 5, 5)]
	[Arguments(100, 50, 50)]
	[Arguments(100, 100, 0)]
	public async Task ClearUnsavedEvents_GivenEventsAndClearEventsCalledWithSpecificVersion_ClearEventsThatAreGreaterThanOrOrEqualToSpecifiedVersion(
		int eventsToCreate,
		int versionToClear,
		int expectedVersion
	)
	{
		// Arrange
		var aggregate = CreateTestAggregate();
		for (var i = 0; i < eventsToCreate; i++)
			aggregate.Increment();

		// Act
		aggregate.ClearUnsavedEvents(versionToClear);

		// Assert
		await Assert.That(aggregate.Details.CurrentVersion).IsEqualTo(expectedVersion);
		await Assert.That(aggregate.GetUnsavedEvents().Count).IsEqualTo(expectedVersion);
	}

	[Test]
	public async Task ClearUnsavedEvents_GivenEventsAndClearEventsCalledWithNull_ClearEventsAllEvents()
	{
		// Arrange
		var aggregate = CreateTestAggregate();

		aggregate.Increment();
		aggregate.Increment();

		// Act
		aggregate.ClearUnsavedEvents();

		// Assert
		await Assert.That(aggregate.GetUnsavedEvents()).IsEmpty();
	}

	[Test]
	[Arguments(4, 8)]
	[Arguments(4, 100)]
	[Arguments(100, 101)]
	[Arguments(100, 1001)]
	public async Task ClearUnsavedEvents_GivenClearValueIsGreaterThanEventDetailsAggregateVersion_SetsUpToSavedVersion(
		int eventsToCreate,
		int versionToClear
	)
	{
		// Arrange
		var aggregate = CreateTestAggregate();
		for (var i = 0; i < eventsToCreate; i++)
			aggregate.Increment();

		// Act
		aggregate.ClearUnsavedEvents(versionToClear);

		// Assert
		await Assert.That(aggregate.Details.SavedVersion).IsEqualTo(0);
	}

	[Test]
	[Arguments(4, 8)]
	[Arguments(4, 100)]
	[Arguments(100, 101)]
	[Arguments(100, 1001)]
	public async Task ClearUnsavedEvents_GivenClearValueIsGreaterThanEventDetailsAggregateVersionAndValuesPreviousSaved_SetsCurrentVersionTo0(
		int eventsToCreate,
		int versionToClear
	)
	{
		// Arrange
		var aggregate = CreateTestAggregate();
		for (var i = 0; i < eventsToCreate; i++)
			aggregate.Increment();

		// Act
		aggregate.ClearUnsavedEvents(versionToClear);

		// Assert
		await Assert.That(aggregate.Details.CurrentVersion).IsEqualTo(0);
		await Assert.That(aggregate.HasUnsavedEvents()).IsFalse();
	}

	[Test]
	[Arguments(0)]
	[Arguments(10)]
	[Arguments(100)]
	public async Task DetailsCurrentVersion_GivenEvents_IncrementsCurrentVersion(int eventCount)
	{
		// Arrange
		var aggregate = CreateTestAggregate();

		// Act
		for (var i = 0; i < eventCount; i++)
			aggregate.Increment();

		// Assert
		await Assert.That(aggregate.Details.CurrentVersion).IsEqualTo(eventCount);
	}

	static TestAggregate CreateTestAggregate(string? id = null)
	{
		TestAggregate aggregate = new() { Details = { Id = id ?? Guid.NewGuid().ToString() } };

		return aggregate;
	}
}
