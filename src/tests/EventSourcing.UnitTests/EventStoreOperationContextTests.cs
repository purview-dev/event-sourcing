namespace Purview.EventSourcing;

public class EventStoreOperationContextTests
{
	[Test]
	public async Task CorrelationId_GivenMultipleAccesses_ReturnsStableValue()
	{
		// Arrange
		EventStoreOperationContext context = new();

		// Act
		var first = context.CorrelationId;
		var second = context.CorrelationId;

		// Assert
		await Assert.That(first).IsEqualTo(second);
		await Assert.That(string.IsNullOrWhiteSpace(first)).IsFalse();
	}

	[Test]
	public async Task CorrelationId_GivenExplicitValue_IsNotOverwritten()
	{
		// Arrange
		EventStoreOperationContext context = new() { CorrelationId = "explicit-correlation-id" };

		// Act
		var value = context.CorrelationId;

		// Assert
		await Assert.That(value).IsEqualTo("explicit-correlation-id");
	}

	[Test]
	public async Task CorrelationId_GivenDistinctContexts_MayDiffer()
	{
		// Arrange
		EventStoreOperationContext first = new();
		EventStoreOperationContext second = new();

		// Act
		var firstValue = first.CorrelationId;
		var secondValue = second.CorrelationId;

		// Assert
		await Assert.That(string.IsNullOrWhiteSpace(firstValue)).IsFalse();
		await Assert.That(string.IsNullOrWhiteSpace(secondValue)).IsFalse();
	}
}
