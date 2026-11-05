using Purview.EventSourcing.Aggregates.Test;

namespace Purview.EventSourcing.Aggregates.Snapshotting;

public sealed class SnapshotStrategyTests
{
	sealed class RecordingSnapshotStrategy : ISnapshotStrategy<TestAggregate>
	{
		public int Calls { get; private set; }

		public bool Result { get; set; }

		public bool ShouldSnapshot(TestAggregate aggregate, int eventsApplied)
		{
			Calls++;
			return Result;
		}
	}

	static TestAggregate CreateAggregate(long savedVersion)
	{
		TestAggregate aggregate = new() { Details = { Id = Guid.NewGuid().ToString() } };
		aggregate.Details.SavedVersion = savedVersion;
		return aggregate;
	}

	#region IntervalSnapshotStrategy

	[Test]
	[Arguments(1, 1, true)]
	[Arguments(2, 1, true)]
	[Arguments(5, 5, true)]
	[Arguments(10, 5, true)] // 10 % 5 == 0
	[Arguments(15, 5, true)] // 15 % 5 == 0
	[Arguments(3, 5, false)] // 3 % 5 != 0
	[Arguments(7, 5, false)] // 7 % 5 != 0
	public async Task IntervalStrategy_GivenSavedVersionAndInterval_ReturnsExpected(
		long savedVersion,
		int interval,
		bool expectedResult
	)
	{
		// Arrange
		IntervalSnapshotStrategy<TestAggregate> strategy = new(interval);
		var aggregate = CreateAggregate(savedVersion);

		// Act
		var result = strategy.ShouldSnapshot(aggregate, eventsApplied: 1);

		// Assert
		await Assert.That(result).IsEqualTo(expectedResult);
	}

	[Test]
	public async Task IntervalStrategy_GivenZeroEventsApplied_ReturnsFalse()
	{
		// Arrange — interval of 1 would normally always snapshot, but 0 events means nothing was saved
		IntervalSnapshotStrategy<TestAggregate> strategy = new(1);
		var aggregate = CreateAggregate(10);

		// Act
		var result = strategy.ShouldSnapshot(aggregate, eventsApplied: 0);

		// Assert
		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task IntervalStrategy_GivenNullAggregate_ThrowsArgumentNullException()
	{
		// Arrange
		IntervalSnapshotStrategy<TestAggregate> strategy = new(1);

		// Act & Assert
		await Assert.That(() => strategy.ShouldSnapshot(null!, eventsApplied: 1)).Throws<ArgumentNullException>();
	}

	[Test]
	[Arguments(0)]
	[Arguments(-1)]
	[Arguments(-100)]
	public async Task IntervalStrategy_GivenIntervalLessThanOne_ThrowsArgumentOutOfRangeException(int interval)
	{
		// Act & Assert
		await Assert
			.That(() => new IntervalSnapshotStrategy<TestAggregate>(interval))
			.Throws<ArgumentOutOfRangeException>();
	}

	#endregion

	#region AlwaysSnapshotStrategy

	[Test]
	[Arguments(1)]
	[Arguments(5)]
	[Arguments(100)]
	public async Task AlwaysStrategy_GivenEventsApplied_AlwaysReturnsTrue(int eventsApplied)
	{
		// Arrange
		AlwaysSnapshotStrategy<TestAggregate> strategy = new();
		var aggregate = CreateAggregate(savedVersion: 1);

		// Act
		var result = strategy.ShouldSnapshot(aggregate, eventsApplied);

		// Assert
		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task AlwaysStrategy_GivenZeroEventsApplied_ReturnsFalse()
	{
		// Arrange
		AlwaysSnapshotStrategy<TestAggregate> strategy = new();
		var aggregate = CreateAggregate(savedVersion: 1);

		// Act
		var result = strategy.ShouldSnapshot(aggregate, eventsApplied: 0);

		// Assert
		await Assert.That(result).IsFalse();
	}

	#endregion

	#region NeverSnapshotStrategy

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(100)]
	public async Task NeverStrategy_GivenAnyInput_AlwaysReturnsFalse(int eventsApplied)
	{
		// Arrange
		NeverSnapshotStrategy<TestAggregate> strategy = new();
		var aggregate = CreateAggregate(savedVersion: 1);

		// Act
		var result = strategy.ShouldSnapshot(aggregate, eventsApplied);

		// Assert
		await Assert.That(result).IsFalse();
	}

	#endregion

	#region SnapshotStrategySelector

	[Test]
	public async Task Selector_GivenAggregateOverride_ReturnsOverride()
	{
		NeverSnapshotStrategy<TestAggregate> expected = new();
		var selector = new SnapshotStrategySelector()
			.SetDefault(new AlwaysSnapshotStrategy<TestAggregate>())
			.Set(expected);

		var selected = selector.Resolve<TestAggregate>();

		await Assert.That(selected).IsSameReferenceAs(expected);
	}

	[Test]
	public async Task Selector_GivenNoAggregateOverride_ReturnsDefault()
	{
		NeverSnapshotStrategy<TestAggregate> expected = new();
		var selector = new SnapshotStrategySelector().SetDefault(expected);

		var selected = selector.Resolve<TestAggregate>();

		await Assert.That(selected).IsSameReferenceAs(expected);
	}

	#endregion

	#region SnapshotStrategyResolver

	[Test]
	public async Task Resolver_GivenContextAggregateStrategy_PrefersContext()
	{
		RecordingSnapshotStrategy defaultStrategy = new() { Result = false };
		RecordingSnapshotStrategy selectorStrategy = new() { Result = false };
		RecordingSnapshotStrategy contextStrategy = new() { Result = true };

		var context = new EventStoreOperationContext
		{
			SnapshotStrategySelector = new SnapshotStrategySelector().SetDefault(selectorStrategy),
		}.SetSnapshotStrategy(contextStrategy);

		var aggregate = CreateAggregate(savedVersion: 1);
		aggregate.Details.CurrentVersion = 2;
		var shouldSnapshot = SnapshotStrategyResolver.ShouldSnapshot(
			aggregate,
			eventsApplied: 1,
			context,
			defaultStrategy
		);

		await Assert.That(shouldSnapshot).IsTrue();
		await Assert.That(contextStrategy.Calls).IsEqualTo(1);
		await Assert.That(selectorStrategy.Calls).IsEqualTo(0);
		await Assert.That(defaultStrategy.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task Resolver_GivenContextSelectorAndNoContextAggregateStrategy_UsesContextSelector()
	{
		RecordingSnapshotStrategy defaultStrategy = new() { Result = false };
		RecordingSnapshotStrategy contextSelectorStrategy = new() { Result = true };
		EventStoreOperationContext context = new()
		{
			SnapshotStrategySelector = new SnapshotStrategySelector().SetDefault(contextSelectorStrategy),
		};

		var aggregate = CreateAggregate(savedVersion: 1);
		aggregate.Details.CurrentVersion = 2;
		var shouldSnapshot = SnapshotStrategyResolver.ShouldSnapshot(
			aggregate,
			eventsApplied: 1,
			context,
			defaultStrategy
		);

		await Assert.That(shouldSnapshot).IsTrue();
		await Assert.That(contextSelectorStrategy.Calls).IsEqualTo(1);
		await Assert.That(defaultStrategy.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task Resolver_GivenStoreSelectorAndNoContextSelector_UsesStoreSelector()
	{
		RecordingSnapshotStrategy defaultStrategy = new() { Result = false };
		RecordingSnapshotStrategy storeSelectorStrategy = new() { Result = true };
		var storeSelector = new SnapshotStrategySelector().SetDefault(storeSelectorStrategy);

		var aggregate = CreateAggregate(savedVersion: 1);
		aggregate.Details.CurrentVersion = 2;
		var shouldSnapshot = SnapshotStrategyResolver.ShouldSnapshot(
			aggregate,
			eventsApplied: 1,
			context: null,
			defaultStrategy,
			storeSelector
		);

		await Assert.That(shouldSnapshot).IsTrue();
		await Assert.That(storeSelectorStrategy.Calls).IsEqualTo(1);
		await Assert.That(defaultStrategy.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task Resolver_GivenNoContextOrSelectors_UsesDefault()
	{
		RecordingSnapshotStrategy defaultStrategy = new() { Result = true };
		var aggregate = CreateAggregate(savedVersion: 1);
		aggregate.Details.CurrentVersion = 2;

		var shouldSnapshot = SnapshotStrategyResolver.ShouldSnapshot(
			aggregate,
			eventsApplied: 1,
			context: null,
			defaultStrategy
		);

		await Assert.That(shouldSnapshot).IsTrue();
		await Assert.That(defaultStrategy.Calls).IsEqualTo(1);
	}

	#endregion
}
