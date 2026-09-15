namespace Purview.EventSourcing.Aggregates;

partial class AggregateBaseTests
{
	[Test]
	public async Task GetUnsavedEvents_GivenNoMutationBetweenCalls_ReturnsStableSnapshot()
	{
		// Arrange
		var aggregate = CreateTestAggregate();
		aggregate.Increment();

		// Act
		var first = aggregate.GetUnsavedEvents();
		var second = aggregate.GetUnsavedEvents();

		// Assert
		await Assert.That(ReferenceEquals(first, second)).IsTrue();
		await Assert.That(first.Count).IsEqualTo(1);
	}

	[Test]
	public async Task GetUnsavedEvents_GivenEventRecordedAfterSnapshot_ReturnsUpdatedEvents()
	{
		// Arrange
		var aggregate = CreateTestAggregate();
		aggregate.Increment();

		var first = aggregate.GetUnsavedEvents();

		// Act
		aggregate.Increment();
		var second = aggregate.GetUnsavedEvents();

		// Assert
		await Assert.That(ReferenceEquals(first, second)).IsFalse();
		await Assert.That(second.Count).IsEqualTo(2);
	}

	[Test]
	public async Task GetUnsavedEvents_GivenClearAfterSnapshot_ReturnsEmptySnapshot()
	{
		// Arrange
		var aggregate = CreateTestAggregate();
		aggregate.Increment();

		_ = aggregate.GetUnsavedEvents();

		// Act
		aggregate.ClearUnsavedEvents();

		// Assert
		await Assert.That(aggregate.GetUnsavedEvents()).IsEmpty();
	}
}
