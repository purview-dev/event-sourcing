using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.ChangeFeed;

namespace Purview.EventSourcing.Contracts;

/// <summary>
/// Provider-agnostic event-store contract tests.
///
/// This file is linked (not compiled) into each provider integration test project. A provider
/// wires it up with a small generic class that combines <c>[GenerateGenericTest]</c>,
/// <c>[ClassDataSource&lt;TF&gt;]</c> and <c>[InheritsTests]</c> against its own fixture.
///
/// Tests only exercise the public event-store contract (<see cref="IEventStoreCore{T}"/>) and
/// observable behavior (save results, rehydrated state, change-feed notifications and event ranges).
/// Provider-internal behavior (telemetry, batch limits, storage layout, cache eviction) belongs in
/// per-provider guard tests.
/// </summary>
public abstract class EventStoreContractTestsBase<TAggregate>
	where TAggregate : class, IAggregateTest, new()
{
	protected abstract IEventStoreCore<TAggregate> CreateEventStore();

	protected abstract IEventStoreCore<TAggregate> CreateEventStore(
		IAggregateChangeFeedNotifier<TAggregate>? changeFeedNotifier
	);

	/// <summary>
	/// Rewrites the stored event-type names for a range of events so they can no longer be resolved.
	/// Used by the unknown-event-type test to simulate schema/type evolution. Providers that cannot
	/// rewrite stored event types should move that test to their own guard suite.
	/// </summary>
	protected virtual Task MarkEventTypesAsUnknownAsync(
		string aggregateId,
		string aggregateType,
		int fromVersion,
		int toVersion,
		string eventType,
		CancellationToken cancellationToken
	) => throw new NotSupportedException($"{GetType().Name} does not support marking stored event types as unknown.");
#pragma warning restore CA1506

	[Test]
	public async Task SaveAsync_GivenNewAggregateWithChanges_SavesAggregate(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		// Act
		bool result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result).IsTrue();
		await Assert.That(aggregate.IsNew()).IsFalse();

		var aggregateFromEventStore = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);

		await Assert.That(aggregateFromEventStore).IsNotNull();
		await Assert.That(aggregateFromEventStore!.Id()).IsEqualTo(aggregate.Id());
		await Assert.That(aggregateFromEventStore.IncrementInt32).IsEqualTo(aggregate.IncrementInt32);
		await Assert.That(aggregateFromEventStore.Details.SavedVersion).IsEqualTo(aggregate.Details.SavedVersion);
		await Assert.That(aggregateFromEventStore.Details.CurrentVersion).IsEqualTo(aggregate.Details.CurrentVersion);
		await Assert.That(aggregateFromEventStore.Details.SnapshotVersion).IsEqualTo(aggregate.Details.SnapshotVersion);
		await Assert.That(aggregateFromEventStore.Details.Etag).IsEqualTo(aggregate.Details.Etag);
	}

	[Test]
	public async Task SaveAsync_GivenAggregateWithNoChanges_DoesNotSave(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);

		var eventStore = CreateEventStore();

		// Act
		bool result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task SaveAsync_GivenAggregateWithNoChanges_DoesNotNotifyChangeFeed(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateChangeNotifier = TestHelpers.CreateAggregateChangeFeedNotified<TAggregate>();

		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);

		var eventStore = CreateEventStore(aggregateChangeNotifier);

		// Act
		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		aggregateChangeNotifier
			.AfterSaveAsync(aggregate, Any<int>(), true, Any<EventRecord[]>(), Any<CancellationToken>())
			.WasNeverCalled();
	}

	[Test]
	[MethodDataSource(typeof(EventStoreContractTestData), nameof(EventStoreContractTestData.SteppedCount))]
	public async Task SaveAsync_GivenAggregateWithChanges_NotifiesChangeFeed(
		int eventsToCreate,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateChangeNotifier = TestHelpers.CreateAggregateChangeFeedNotified<TAggregate>();

		var beforeWasCalled = false;
		var afterWasCalled = false;
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		for (var i = 0; i < eventsToCreate; i++)
			aggregate.AppendString($"{i + 1} of {eventsToCreate}(s) to created.");

		var eventStore = CreateEventStore(aggregateChangeNotifier);

		aggregateChangeNotifier
			.BeforeSaveAsync(aggregate, true, Any<CancellationToken>())
			.Callback(
				(a, _, _) =>
				{
					a.AppendString(nameof(IAggregateChangeFeedProcessor.AfterSaveAsync));

					beforeWasCalled = true;
				}
			);

		aggregateChangeNotifier
			.AfterSaveAsync(aggregate, 0, true, Any<EventRecord[]>(), Any<CancellationToken>())
			.Callback(() => afterWasCalled = true);

		// Act
		bool result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(beforeWasCalled).IsTrue();
		await Assert.That(afterWasCalled).IsTrue();

		aggregateChangeNotifier.BeforeSaveAsync(aggregate, true, Any<CancellationToken>()).WasCalled(Times.Once);

		aggregateChangeNotifier
			.AfterSaveAsync(
				aggregate,
				Any<int>(),
				true,
				Is<EventRecord[]>(events => events!.Length == eventsToCreate),
				Any<CancellationToken>()
			)
			.WasCalled(Times.Once);
	}

	[Test]
	public async Task SaveAsync_GivenAggregateWithDataAnnotationsAndInvalidProperties_NoChangesAreMadeAndNotSaved(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId, a => a.SetValidatedProperty(-1));

		var eventStore = CreateEventStore();

		// Act
		var result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.Saved).IsFalse();
		await Assert.That(result.IsValid).IsFalse();
		await Assert.That((bool)result).IsFalse();
		await Assert.That(result.ValidationResult.Failures).HasSingleItem();
		await Assert
			.That(result.ValidationResult.Failures.Single().PropertyName)
			.IsEqualTo(nameof(IAggregateTest.IncrementInt32));
	}

	[Test]
	public async Task SaveAsync_GivenAggregateWithComplexProperty_SavesEventWithComplexProperty(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);

		EventStoreTestSeed.SetRandomComplexProperty(aggregate);

		var eventStore = CreateEventStore();
		var result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await Assert.That(result.Saved).IsTrue();

		// Act
		var aggregateGetResult = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);

		// Assert
		await EventStoreTestSeed.AssertComplexPropertyMatches(aggregate, aggregateGetResult);
	}

	[Test]
	public async Task SaveAsync_GivenNewAggregateWithLargeChanges_SavesAggregateWithLargeEventRecord(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);

		var value = EventStoreTestSeed.BuildLargeString();
		aggregate.AppendString(value);

		var eventStore = CreateEventStore();

		// Act
		bool result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result).IsTrue();
		await Assert.That(aggregate.IsNew()).IsFalse();

		var aggregateFromEventStore = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);

		await Assert
			.That((aggregateFromEventStore?.StringProperty ?? string.Empty).Length)
			.IsEqualTo(aggregate.StringProperty.Length);

		await Assert.That(aggregateFromEventStore?.StringProperty).IsEqualTo(aggregate.StringProperty);
	}

	[Test]
	public async Task SaveAsync_GivenNewAggregateWithLargeChangesAndNoSnapshot_ReadsAggregateFromEvents(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);

		var value = EventStoreTestSeed.BuildLargeString();
		aggregate.AppendString(value);

		var eventStore = CreateEventStore();

		// Act
		bool result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result).IsTrue();
		await Assert.That(aggregate.IsNew()).IsFalse();

		// Re-read the aggregate straight from the event records, bypassing the snapshot.
		var aggregateFromEventStore = await eventStore.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SkipSnapshot = true },
			cancellationToken: cancellationToken
		);

		await Assert
			.That((aggregateFromEventStore?.StringProperty ?? string.Empty).Length)
			.IsEqualTo(aggregate.StringProperty.Length);

		await Assert.That(aggregateFromEventStore?.StringProperty).IsEqualTo(aggregate.StringProperty);
	}

	[Test]
	[MethodDataSource(typeof(EventStoreContractTestData), nameof(EventStoreContractTestData.TooManyEventCount))]
	public async Task SaveAsync_GivenEventCountIsGreaterThanMaximumNumberOfAllowedEventsInSaveOperation_ThrowsException(
		int eventsToGenerate,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		for (var i = 0; i < eventsToGenerate; i++)
			aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		// Act / Assert
		await EventStoreTestSeed.AssertSaveThrowsArgumentOutOfRangeException(eventStore, aggregate, cancellationToken);
	}

	[Test]
	[MethodDataSource(typeof(EventStoreContractTestData), nameof(EventStoreContractTestData.SteppedCount))]
	public async Task GetAsync_GivenAnAggregateWithSavedEventsButNoSnapshot_RecreatesAggregate(
		int eventsToCreate,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = EventStoreTestSeed.BuildAggregateWithIncrementEvents<TAggregate>(aggregateId, eventsToCreate);

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Act
		var result = await eventStore.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SkipSnapshot = true },
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.IsNew()).IsFalse();
		await Assert.That(result.Id()).IsEqualTo(aggregate.Id());
		await Assert.That(result.IncrementInt32).IsEqualTo(aggregate.IncrementInt32);
		await Assert.That(result.Details.SavedVersion).IsEqualTo(aggregate.Details.SavedVersion);
		await Assert.That(result.Details.CurrentVersion).IsEqualTo(aggregate.Details.CurrentVersion);
		await Assert
			.That(result.Details.SnapshotVersion)
			.IsEqualTo(0)
			.Because("The snapshot was skipped, so the aggregate was reconstructed purely from events.");
	}

	[Test]
	[MethodDataSource(typeof(EventStoreContractTestData), nameof(EventStoreContractTestData.SnapshotEventCount))]
	public async Task GetAsync_GivenAnAggregateWithMoreEventsThanTheSnapshot_RecreatesAggregate(
		int eventsToCreate,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const int eventCountOffset = 4;
		var totalEventsToCreate = eventsToCreate + eventCountOffset;

		var aggregateId = $"{Guid.NewGuid()}";

		var eventStore = CreateEventStore();

		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		for (var i = 0; i < eventsToCreate; i++)
			aggregate.IncrementInt32Value();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		for (var i = 0; i < eventCountOffset; i++)
			aggregate.IncrementInt32Value();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Act
		var result = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.IsNew()).IsFalse();
		await Assert.That(result.IncrementInt32).IsEqualTo(totalEventsToCreate);
		await Assert.That(result.Details.SavedVersion).IsEqualTo(totalEventsToCreate);
	}

	// This is testing that the aggregate is still correct after having an event type removed (in this case,
	// it deserializes, but it's not registered any longer),
	// this is often due to the schema changes and the event not being required anymore, but the
	// event record still (correctly) exists.
	[Test]
	[MethodDataSource(
		typeof(EventStoreContractTestData),
		nameof(EventStoreContractTestData.SteppedEventCountWithOldEventCount)
	)]
	public async Task GetAsync_GivenAnAggregateWithNonRegisteredEventType_RecreatesAggregateAndLogsCannotApplyEvent(
		int eventsToCreate,
		int numberOfOldEventsToCreate,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var totalEvents = eventsToCreate + numberOfOldEventsToCreate;

		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = EventStoreTestSeed.BuildAggregateWithOldEvents<TAggregate>(
			aggregateId,
			eventsToCreate,
			numberOfOldEventsToCreate
		);

		var eventStore = CreateEventStore();

		// Act
		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Get without using the snapshot, just from the event records.
		var result = await eventStore.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SkipSnapshot = true },
			cancellationToken: cancellationToken
		);

		// Assert
		await EventStoreTestSeed.AssertRecreatedWithTotals(result, aggregate, totalEvents);
	}

	// This is testing that the aggregate is still correct after an event type cannot be found - removed
	// from the assembly/ failure to load the type -
	// this is often due to the schema changes and the event not being required anymore, but the
	// event record still (correctly) exists.
	[Test]
	[MethodDataSource(
		typeof(EventStoreContractTestData),
		nameof(EventStoreContractTestData.SteppedEventCountWithOldEventCount)
	)]
	public async Task GetAsync_GivenAnAggregateWithUnknownEventType_RecreatesAggregateAndLogsUnknown(
		int eventsToCreate,
		int numberOfOldEventsToCreate,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string unknownEventType = "an-unknown-type";

		var totalEvents = eventsToCreate + numberOfOldEventsToCreate;

		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = EventStoreTestSeed.BuildAggregateWithOldEvents<TAggregate>(
			aggregateId,
			eventsToCreate,
			numberOfOldEventsToCreate
		);

		var eventStore = CreateEventStore();

		// Act
		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Update existing events to make them unknown types effectively.
		await MarkEventTypesAsUnknownAsync(
			aggregateId,
			aggregate.AggregateType,
			eventsToCreate + 1,
			totalEvents,
			unknownEventType,
			cancellationToken
		);

		// Get without using the snapshot, just from the event records.
		var result = await eventStore.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SkipSnapshot = true },
			cancellationToken: cancellationToken
		);

		// Assert
		await EventStoreTestSeed.AssertRecreatedWithTotals(result, aggregate, totalEvents);
	}

	[Test]
	public async Task GetAsync_GivenAggregateIsDeletedAndDeletedModeIsSetToThrow_ThrowsEventStoreAggregateDeletedException(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await eventStore.DeleteAsync(aggregate, cancellationToken: cancellationToken);

		// Act / Assert
		await EventStoreTestSeed.AssertGetAsyncThrowsDeletedException(eventStore, aggregateId, cancellationToken);
	}

	[Test]
	[MethodDataSource(typeof(EventStoreContractTestData), nameof(EventStoreContractTestData.SteppedCount))]
	public async Task GetAtAsync_GivenAnAggregateWithSavedEvents_RecreatesAggregateToPreviousVersion(
		int previousEventsToCreate,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		for (var i = 0; i < previousEventsToCreate; i++)
			aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Add an extra event to push it past the requested number of events.
		aggregate.IncrementInt32Value();
		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		await Assert.That(aggregate.IncrementInt32).IsEqualTo(previousEventsToCreate + 1);

		// Act
		var result = await eventStore.GetAtAsync(
			aggregateId,
			version: previousEventsToCreate,
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.IncrementInt32).IsEqualTo(previousEventsToCreate);
		await Assert.That(result.Details.SavedVersion).IsEqualTo(previousEventsToCreate);
		await Assert.That(result.Details.CurrentVersion).IsEqualTo(previousEventsToCreate);
		await Assert.That(result.Details.Locked).IsTrue();
	}

	[Test]
	public async Task GetOrCreateAsync_GivenAggregateDoesNotExist_CreatesNewAggregate(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var eventStore = CreateEventStore();

		// Act
		var result = await eventStore.GetOrCreateAsync(
			aggregateId,
			operationContext: null,
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Id()).IsEqualTo(aggregateId);
		await Assert.That(result.IsNew()).IsTrue();
	}

	[Test]
	public async Task ExistsAsync_GivenSavedAggregate_ReturnsExists(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Act
		var result = await eventStore.ExistsAsync(aggregateId, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.DoesExist).IsTrue();
		await Assert.That(result.Status).IsEqualTo(ExistsStatus.Exists);
	}

	[Test]
	public async Task GetDeletedAsync_GivenDeletedAggregate_ReturnsAggregate(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await eventStore.DeleteAsync(aggregate, cancellationToken: cancellationToken);

		// Act
		var aggregateResult = await eventStore.GetDeletedAsync(aggregateId, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(aggregateResult).IsNotNull();
		await Assert.That(aggregateResult!.Details.IsDeleted).IsTrue();
		await Assert.That(aggregateResult.Details.SavedVersion).IsEqualTo(2);
	}

	[Test]
	public async Task IsDeletedAsync_GivenDeletedAggregates_ReturnsTrue(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await eventStore.DeleteAsync(aggregate, cancellationToken: cancellationToken);

		// Act
		var result = await eventStore.IsDeletedAsync(aggregateId, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task IsDeletedAsync_GivenNonDeletedAggregates_ReturnsFalse(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Act
		var result = await eventStore.IsDeletedAsync(aggregateId, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task RestoreAsync_GivenPreviouslySavedAndDeletedAggregate_MarksAsNotDeleted(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await eventStore.DeleteAsync(aggregate, cancellationToken: cancellationToken);

		aggregate = await eventStore.GetDeletedAsync(aggregateId, cancellationToken);
		await Assert.That(aggregate).IsNotNull();

		// Act
		var result = await eventStore.RestoreAsync(aggregate!, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result).IsTrue();
		await Assert.That(aggregate.Details.IsDeleted).IsFalse();
		await Assert.That(aggregate.Details.SavedVersion).IsEqualTo(3);
	}

	[Test]
	public async Task DeleteAsync_GivenPreviouslySavedAggregate_MarksAsDeleted(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		var aggregateResult =
			await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken)
			?? throw new NullReferenceException();

		// Act
		var result = await eventStore.DeleteAsync(aggregateResult, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result).IsTrue();
		await Assert.That(aggregateResult.Details.IsDeleted).IsTrue();
		await Assert.That(aggregateResult.Details.SavedVersion).IsEqualTo(2);
	}

	[Test]
	public async Task DeleteAsync_GivenDelete_NotifiesChangeFeed(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateChangeNotifier = TestHelpers.CreateAggregateChangeFeedNotified<TAggregate>();

		var beforeWasCalled = false;
		var afterWasCalled = false;
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore(aggregateChangeNotifier);

		aggregateChangeNotifier
			.BeforeDeleteAsync(aggregate, Any<CancellationToken>())
			.Callback(() => beforeWasCalled = true);

		aggregateChangeNotifier
			.AfterDeleteAsync(aggregate, Any<CancellationToken>())
			.Callback(() => afterWasCalled = true);

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Act
		var result = await eventStore.DeleteAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(beforeWasCalled).IsTrue();
		await Assert.That(afterWasCalled).IsTrue();

		aggregateChangeNotifier.BeforeDeleteAsync(aggregate, Any<CancellationToken>()).WasCalled(Times.Once);
		aggregateChangeNotifier.AfterDeleteAsync(aggregate, Any<CancellationToken>()).WasCalled(Times.Once);
	}

	[Test]
	public async Task DeleteAsync_GivenAggregateExists_PermanentlyDeletesAllData(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		aggregate = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);
		await Assert.That(aggregate).IsNotNull();

		// Act
		var result = await eventStore.DeleteAsync(
			aggregate!,
			new EventStoreOperationContext { PermanentlyDelete = true },
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsTrue();

		// The aggregate and all of its data are gone.
		await Assert.That(await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsNull();
	}

	[Test]
	public async Task DeleteAsync_GivenAggregateExistsWithLargeEvent_PermanentlyDeletesAllData(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		aggregate.AppendString(EventStoreTestSeed.BuildLargeString());

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		aggregate = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);
		await Assert.That(aggregate).IsNotNull();

		// Act
		var result = await eventStore.DeleteAsync(
			aggregate!,
			new EventStoreOperationContext { PermanentlyDelete = true },
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsTrue();

		// The aggregate and all of its data are gone.
		await Assert.That(await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsNull();
	}

	[Test]
	[MethodDataSource(typeof(EventStoreContractTestData), nameof(EventStoreContractTestData.RequestedRangeOfEvents))]
	public async Task GetEventRangeAsync_GivenARequestedRangeOfEvents_EventsAreReturnsInCorrectOrder(
		int eventsToCreate,
		int startEvent,
		int? endEvent,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";

		var eventStore = CreateEventStore();

		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		for (var i = 0; i < eventsToCreate; i++)
			aggregate.IncrementInt32Value();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Act
		var results = eventStore.GetEventRangeAsync(
			aggregateId,
			startEvent,
			endEvent,
			cancellationToken: cancellationToken
		);

		// Assert
		await foreach ((var eventRecord, _) in results)
			await Assert.That(eventRecord.Metadata.AggregateVersion).IsEqualTo(startEvent++);
	}

	[Test]
	[MethodDataSource(
		typeof(EventStoreContractTestData),
		nameof(EventStoreContractTestData.RequestedRangeOfEventsWithExpectedEventCount)
	)]
	public async Task GetEventRangeAsync_GivenARequestedRangeOfEvents_GetsEventsRequested(
		int eventsToCreate,
		int startEvent,
		int? endEvent,
		int expectedEventCount,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var eventStore = CreateEventStore();

		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		for (var i = 0; i < eventsToCreate; i++)
			aggregate.IncrementInt32Value();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Act
		var results = eventStore.GetEventRangeAsync(
			aggregateId,
			startEvent,
			endEvent,
			cancellationToken: cancellationToken
		);

		// Assert
		List<EventRecord> eventList = [];
		await foreach ((var eventRecord, _) in results)
			eventList.Add(eventRecord);

		await Assert.That(eventList.Count).IsEqualTo(expectedEventCount);
	}

	[Test]
	[MethodDataSource(typeof(EventStoreContractTestData), nameof(EventStoreContractTestData.SteppedCount))]
	public async Task GetAggregateIdsAsync_GivenNAggregatesInTheStore_CorrectlyReturnsTheirIds(
		int aggregateCount,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		List<string> generatedIds = [];
		var eventStore = CreateEventStore();

		for (var i = 0; i < aggregateCount; i++)
		{
			var aggregateId = $"{Guid.NewGuid()}";
			var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
			aggregate.IncrementInt32Value();

			await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

			generatedIds.Add(aggregateId);
		}

		// Act
		List<string> returnedIds = [];
		await foreach (var id in eventStore.GetAggregateIdsAsync(true, cancellationToken: cancellationToken))
			returnedIds.Add(id);

		// Assert
		await Assert.That(returnedIds.Count).IsEqualTo(aggregateCount);
		await Assert.That(generatedIds).IsEquivalentTo(returnedIds);
	}

	[Test]
	[MethodDataSource(
		typeof(EventStoreContractTestData),
		nameof(EventStoreContractTestData.SteppedAggregateCountWithDeletedAggregateCount)
	)]
	public async Task GetAggregateIdsAsync_GivenNonDeletedAggregatesAndDeletedAggregatesInTheStoreAndRequestingAll_CorrectlyReturnsAllIds(
		int nonDeletedAggregateIdCount,
		int deletedAggregateIdCount,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		List<string> generatedIds = [];
		var eventStore = CreateEventStore();

		for (var i = 0; i < nonDeletedAggregateIdCount; i++)
		{
			var aggregateId = $"{Guid.NewGuid()}";
			var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
			aggregate.IncrementInt32Value();

			await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

			generatedIds.Add(aggregateId);
		}

		for (var i = 0; i < deletedAggregateIdCount; i++)
		{
			var aggregateId = $"{Guid.NewGuid()}";
			var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
			aggregate.IncrementInt32Value();

			await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
			await eventStore.DeleteAsync(aggregate, cancellationToken: cancellationToken);

			generatedIds.Add(aggregateId);
		}

		// Act
		List<string> returnedIds = [];
		await foreach (var id in eventStore.GetAggregateIdsAsync(true, cancellationToken: cancellationToken))
			returnedIds.Add(id);

		// Assert
		await Assert.That(returnedIds.Count).IsEqualTo(deletedAggregateIdCount + nonDeletedAggregateIdCount);
		await Assert.That(generatedIds).IsEquivalentTo(returnedIds);
	}

	[Test]
	[MethodDataSource(
		typeof(EventStoreContractTestData),
		nameof(EventStoreContractTestData.SteppedAggregateCountWithDeletedAggregateCount)
	)]
	public async Task GetAggregateIdsAsync_GivenNonDeletedAggregatesAndDeletedAggregatesInTheStoreAndRequestingOnlyNonDeleted_CorrectlyReturnsNonDeletedIdsOnly(
		int nonDeletedAggregateIdCount,
		int deletedAggregateIdCount,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		List<string> generatedIds = [];
		var eventStore = CreateEventStore();

		for (var i = 0; i < nonDeletedAggregateIdCount; i++)
		{
			var aggregateId = $"{Guid.NewGuid()}";
			var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
			aggregate.IncrementInt32Value();

			await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

			generatedIds.Add(aggregateId);
		}

		for (var i = 0; i < deletedAggregateIdCount; i++)
		{
			var aggregateId = $"{Guid.NewGuid()}";
			var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
			aggregate.IncrementInt32Value();

			await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
			await eventStore.DeleteAsync(aggregate, cancellationToken: cancellationToken);
		}

		// Act
		List<string> returnedIds = [];
		await foreach (var id in eventStore.GetAggregateIdsAsync(false, cancellationToken: cancellationToken))
			returnedIds.Add(id);

		// Assert
		await Assert.That(returnedIds.Count).IsEqualTo(nonDeletedAggregateIdCount);
		await Assert.That(generatedIds).IsEquivalentTo(returnedIds);
	}
}
