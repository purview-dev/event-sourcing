using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Test;

namespace Purview.EventSourcing;

public sealed class IEventStoreExtensionsEnlistTests
{
	[Test]
	public async Task Enlist_GivenNullEventStore_ThrowsArgumentNullException()
	{
		// Arrange
		var aggregate = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		aggregate.Increment();

		// Act & Assert
		await Assert.That(() => ((IEventStore)null!).Enlist(aggregate)).Throws<ArgumentNullException>();
	}

	[Test]
	public async Task Enlist_GivenSingleAggregate_ReturnsPreparedTransaction(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregate = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		aggregate.Increment();

		var eventStore = IEventStore.Mock();
		eventStore
			.SaveAsync(Any<TestAggregate>(), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.Returns(new SaveOutcome<TestAggregate>(aggregate, SaveOutcomeKind.Written));

		// Act
		await using var transaction = eventStore.Enlist(aggregate);
		var result = await transaction.CommitAsync(cancellationToken);

		// Assert
		await Assert.That(result.Success).IsTrue();
		await Assert.That(result.Results).Count().IsEqualTo(1);
		eventStore
			.SaveAsync(Is(aggregate), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.WasCalled(Times.Once);
	}

	[Test]
	public async Task Enlist_GivenMultipleAggregates_EnlistsAllInTransaction(CancellationToken cancellationToken)
	{
		// Arrange
		var agg1 = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		agg1.Increment();

		var agg2 = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		agg2.RecordEvent();

		var eventStore = IEventStore.Mock();
		eventStore
			.SaveAsync(Any<TestAggregate>(), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.Returns(static (a, _, _) => new SaveOutcome<TestAggregate>(a, SaveOutcomeKind.Written));

		// Act
		await using var transaction = eventStore.Enlist(agg1, agg2);
		var result = await transaction.CommitAsync(cancellationToken);

		// Assert
		await Assert.That(result.Success).IsTrue();
		await Assert.That(result.Results).Count().IsEqualTo(2);
		eventStore
			.SaveAsync(Is(agg1), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.WasCalled(Times.Once);
		eventStore
			.SaveAsync(Is(agg2), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.WasCalled(Times.Once);
	}

	[Test]
	public async Task Enlist_WithCorrelationId_PropagatesCorrelationIdToTransaction(CancellationToken cancellationToken)
	{
		// Arrange
		var correlationId = "my-correlation-id";
		var aggregate = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		aggregate.Increment();

		var eventStore = IEventStore.Mock();
		eventStore
			.SaveAsync(Any<TestAggregate>(), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.Returns(new SaveOutcome<TestAggregate>(aggregate, SaveOutcomeKind.Written));

		// Act
		await using var transaction = eventStore.Enlist(correlationId, aggregate);
		await transaction.CommitAsync(cancellationToken);

		// Assert — the transaction uses the provided correlation ID
		await Assert.That(transaction.CorrelationId).IsEqualTo(correlationId);
		eventStore
			.SaveAsync(Is(aggregate), ctx => ctx!.CorrelationId == correlationId, Any<CancellationToken>())
			.WasCalled(Times.Once);
	}

	[Test]
	public async Task Enlist_WithNullCorrelationId_GeneratesCorrelationId()
	{
		var currentActivity = System.Diagnostics.Activity.Current;
		System.Diagnostics.Activity.Current = null;
		try
		{
			// Arrange
			var aggregate = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
			aggregate.Increment();

			var eventStore = IEventStore.Mock();

			// Act
			await using var transaction = eventStore.Enlist(correlationId: null, aggregate);

			// Assert — a non-empty correlation ID was auto-generated
			await Assert.That(transaction.CorrelationId).IsNotEmpty();
			await Assert.That(Guid.TryParse(transaction.CorrelationId, out _)).IsTrue();
		}
		finally
		{
			System.Diagnostics.Activity.Current = currentActivity;
		}
	}

	[Test]
	public async Task Enlist_WithOperationContext_UsesContextCorrelationIdForTransaction(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var correlationId = "context-correlation";
		EventStoreOperationContext context = new() { CorrelationId = correlationId };

		var aggregate = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		aggregate.Increment();

		var eventStore = IEventStore.Mock();
		eventStore
			.SaveAsync(Any<TestAggregate>(), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.Returns(new SaveOutcome<TestAggregate>(aggregate, SaveOutcomeKind.Written));

		// Act
		await using var transaction = eventStore.Enlist(context, aggregate);
		await transaction.CommitAsync(cancellationToken);

		// Assert — the transaction inherits the correlation ID from the context
		await Assert.That(transaction.CorrelationId).IsEqualTo(correlationId);
		eventStore
			.SaveAsync(Is(aggregate), ctx => ctx!.CorrelationId == correlationId, Any<CancellationToken>())
			.WasCalled(Times.Once);
	}

	[Test]
	public async Task Enlist_WithNullOperationContext_GeneratesCorrelationId()
	{
		// Arrange
		var aggregate = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		aggregate.Increment();

		var eventStore = IEventStore.Mock();

		// Act
		await using var transaction = eventStore.Enlist((EventStoreOperationContext?)null, aggregate);

		// Assert — auto-generated correlation ID
		await Assert.That(transaction.CorrelationId).IsNotEmpty();
	}

	[Test]
	public async Task Enlist_WithCorrelationIdAndNullAggregatesArray_ThrowsArgumentNullException()
	{
		// Arrange
		var eventStore = IEventStore.Mock();

		// Act & Assert
		await Assert
			.That(() => eventStore.Enlist<TestAggregate>(correlationId: "corr", aggregates: null!))
			.Throws<ArgumentNullException>();
	}

	[Test]
	public async Task Enlist_WithOperationContextAndNullAggregatesArray_ThrowsArgumentNullException()
	{
		// Arrange
		var eventStore = IEventStore.Mock();
		EventStoreOperationContext context = new();

		// Act & Assert
		await Assert
			.That(() => eventStore.Enlist<TestAggregate>(context, aggregates: null!))
			.Throws<ArgumentNullException>();
	}

	[Test]
	public async Task Enlist_WithNoAggregates_ReturnsEmptyTransactionThatCommitsSuccessfully(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var eventStore = IEventStore.Mock();

		// Act
		await using var transaction = eventStore.Enlist();
		var result = await transaction.CommitAsync(cancellationToken);

		// Assert — nothing to save, result has no entries but commits without error
		await Assert.That(result.Results).Count().IsEqualTo(0);
		eventStore
			.SaveAsync(Any<TestAggregate>(), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.WasNeverCalled();
	}

	[Test]
	public async Task Enlist_WithOperationContextAndMultipleAggregates_AppliesSameContextToAll(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		EventStoreOperationContext context = new() { CorrelationId = "shared" };

		var agg1 = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		agg1.Increment();

		var agg2 = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		agg2.RecordEvent();

		var eventStore = IEventStore.Mock();
		eventStore
			.SaveAsync(Any<TestAggregate>(), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.Returns(static (a, _, _) => new SaveOutcome<TestAggregate>(a, SaveOutcomeKind.Written));

		// Act
		await using var transaction = eventStore.Enlist(context, agg1, agg2);
		await transaction.CommitAsync(cancellationToken);

		// Assert — both saves received the same context
		eventStore
			.SaveAsync(Is(agg1), ctx => ctx!.CorrelationId == "shared", Any<CancellationToken>())
			.WasCalled(Times.Once);
		eventStore
			.SaveAsync(Is(agg2), ctx => ctx!.CorrelationId == "shared", Any<CancellationToken>())
			.WasCalled(Times.Once);
	}
}
