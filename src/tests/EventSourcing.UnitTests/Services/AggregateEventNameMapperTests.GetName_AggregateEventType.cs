namespace Purview.EventSourcing;

partial class AggregateEventNameMapperTests
{
	[Test]
	public async Task GetName_GivenEventTypeEndingWithEvent_ReturnsMappedName()
	{
		// Arrange
		var mapper = CreateMapper<CorrectlyNamedAggregate>();
		var eventType = typeof(EventTypeEndingInEvent);

		// Act
		var result = mapper.GetName<CorrectlyNamedAggregate>(eventType);

		// Assert
		await Assert.That(result).IsEqualTo($"{CorrectlyNamedAggregateName}.event-type-ending-in");
	}

	[Test]
	public async Task GetName_GivenEventTypeNotEndingInEvent_ReturnsTypeName()
	{
		// Arrange
		var mapper = CreateMapper<CorrectlyNamedAggregate>();
		var eventType = typeof(EventTypeNotEndingInEvent2);

		// Act
		var result = mapper.GetName<CorrectlyNamedAggregate>(eventType);

		// Assert
		await Assert.That(result).IsEqualTo(typeof(EventTypeNotEndingInEvent2).FullName);
	}
}
