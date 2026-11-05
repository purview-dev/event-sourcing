namespace Purview.EventSourcing;

public class TypeNameHelperTests
{
	[Test]
	[Arguments("test", "TESTAggregate", "Aggregate")]
	[Arguments("test", "TestAggregate", "Aggregate")]
	[Arguments("test", "testAggregate", "Aggregate")]
	[Arguments("kieron", "KieronAggregate", "Aggregate")]
	[Arguments("kieron", "KIERONAggregate", "Aggregate")]
	[Arguments("kieron", "kieronAggregate", "Aggregate")]
	[Arguments("test", "TESTEvent", "Event")]
	[Arguments("test", "TestEvent", "Event")]
	[Arguments("test", "testEvent", "Event")]
	[Arguments("kieron", "KieronEvent", "Event")]
	[Arguments("kieron", "KIERONEvent", "Event")]
	[Arguments("kieron", "kieronEvent", "Event")]
	public async Task GetName_GivenAggregateTypeEndsWithTrimNamePart_ReturnsLoweredTypeNameWithoutTrimNamePart(
		string expectation,
		string aggregateName,
		string trimNamePart
	)
	{
		// Arrange
		var type = Type.Mock();
		type.Name.Returns(aggregateName);
		type.FullName.Returns((string?)null);

		// Act
		var result = TypeNameHelper.GetName(type, trimNamePart);

		// Assert
		await Assert.That(result).IsEqualTo(expectation);
	}

	[Test]
	[Arguments("test-kieron", "TestKieronAggregate", "Aggregate")]
	[Arguments("kieron-test", "KieronTestAggregate", "Aggregate")]
	[Arguments("kieron-test", "KieronTESTAggregate", "Aggregate")]
	[Arguments("learn-html-test", "LearnHTMLTestAggregate", "Aggregate")]
	[Arguments("test-kieron", "TestKieronEvent", "Event")]
	[Arguments("kieron-test", "KieronTestEvent", "Event")]
	[Arguments("kieron-test", "KieronTESTEvent", "Event")]
	[Arguments("learn-html-test", "LearnHTMLTestEvent", "Event")]
	public async Task GetName_GivenAggregateTypeEndsWithTrimNamePartAndHasTitleCasedName_ReturnsLoweredTypeNameWithoutAggregateSplitByDash(
		string expectation,
		string aggregateName,
		string trimNamePart
	)
	{
		// Arrange
		var type = Type.Mock();
		type.Name.Returns(aggregateName);
		type.FullName.Returns((string?)null);

		// Act
		var result = TypeNameHelper.GetName(type, trimNamePart);

		// Assert
		await Assert.That(result).IsEqualTo(expectation);
	}

	[Test]
	[Arguments("TEST", "TEST", "Aggregate")]
	[Arguments("Test", "Test", "Aggregate")]
	[Arguments("test", "test", "Aggregate")]
	[Arguments("Kieron", "Kieron", "Aggregate")]
	[Arguments("KIERON", "KIERON", "Aggregate")]
	[Arguments("kieron", "kieron", "Aggregate")]
	public async Task GetName_GivenTypeDoesNotEndsWithTrimNamePartAndFallThroughToFullTypeNameIsTrue_ReturnsTypeFullName(
		string expectation,
		string aggregateName,
		string trimNamePart
	)
	{
		// Arrange
		var type = Type.Mock();
		type.Name.Returns("anything");
		type.FullName.Returns(aggregateName);

		// Act
		var result = TypeNameHelper.GetName(type, trimNamePart, fallThroughToFullTypeName: true);

		// Assert
		await Assert.That(result).IsEqualTo(expectation);
	}

	[Test]
	[Arguments("TEST", "TEST", "Aggregate")]
	[Arguments("Test", "Test", "Aggregate")]
	[Arguments("test", "test", "Aggregate")]
	[Arguments("Kieron", "Kieron", "Aggregate")]
	[Arguments("KIERON", "KIERON", "Aggregate")]
	[Arguments("kieron", "kieron", "Aggregate")]
	public async Task GetName_GivenTypeDoesNotEndsWithTrimNamePart_ReturnsTypeName(
		string expectation,
		string aggregateName,
		string trimNamePart
	)
	{
		// Arrange
		var type = Type.Mock();
		type.Name.Returns(aggregateName);
		type.FullName.Returns((string?)null);

		// Act
		var result = TypeNameHelper.GetName(type, trimNamePart);

		// Assert
		await Assert.That(result).IsEqualTo(expectation);
	}
}
