using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.Tenancy;

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
	/// Creates a store bound to a tenant scope, sharing storage with every other store created for the
	/// same <paramref name="runId"/>.
	/// </summary>
	/// <param name="scope">The tenant the store runs under.</param>
	/// <param name="runId">Identifies the storage the stores share.</param>
	/// <remarks>
	/// <para>
	/// Abstract rather than virtual, so a provider cannot be added without stating how it is scoped. The
	/// tenancy tests below are the reason: they were previously per-provider copies, and a provider that
	/// simply never had one looked just as green as a provider that passed.
	/// </para>
	/// <para>
	/// **Sharing storage is the whole point.** Two stores built with different run identifiers use
	/// different tables, databases or containers, so "the other tenant cannot see it" passes while
	/// proving nothing.
	/// </para>
	/// </remarks>
	protected abstract IEventStoreCore<TAggregate> CreateTenantScopedEventStore(TenantScope scope, Guid runId);

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

	async Task<string> SeedForTenantAsync(Guid runId, string tenantId, CancellationToken cancellationToken)
	{
		var aggregateId = $"{Guid.NewGuid()}";
		var store = CreateTenantScopedEventStore(TenantScope.For(tenantId), runId);

		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var saved = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

		await Assert.That(saved.IsSuccess).IsTrue();

		return aggregateId;
	}

	[Test]
	public async Task GetAsync_GivenAnotherTenantsAggregate_DoesNotReturnIt(CancellationToken cancellationToken)
	{
		var runId = Guid.NewGuid();
		var aggregateId = await SeedForTenantAsync(runId, "acme", cancellationToken);

		var owner = CreateTenantScopedEventStore(TenantScope.For("acme"), runId);
		var other = CreateTenantScopedEventStore(TenantScope.For("globex"), runId);

		await Assert.That((await owner.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsSuccess).IsTrue();
		await Assert.That((await other.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsFailure).IsTrue();
	}

	[Test]
	public async Task GetAsync_GivenSystemScope_ReadsAcrossTenants(CancellationToken cancellationToken)
	{
		var runId = Guid.NewGuid();
		var aggregateId = await SeedForTenantAsync(runId, "acme", cancellationToken);

		var system = CreateTenantScopedEventStore(TenantScope.System, runId);

		await Assert
			.That((await system.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsSuccess)
			.IsTrue();
	}

	// The distributed cache is read before the tenant-filtered storage lookup, so an unqualified cache
	// key serves one tenant's aggregate to another ahead of every storage filter. Reading twice as the
	// owner guarantees the entry is cached before the other tenant asks for it.
	[Test]
	public async Task GetAsync_GivenACachedAggregate_DoesNotServeItToAnotherTenant(CancellationToken cancellationToken)
	{
		var runId = Guid.NewGuid();
		var aggregateId = await SeedForTenantAsync(runId, "acme", cancellationToken);

		var owner = CreateTenantScopedEventStore(TenantScope.For("acme"), runId);
		await owner.GetAsync(aggregateId, cancellationToken: cancellationToken);
		await Assert.That((await owner.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsSuccess).IsTrue();

		var other = CreateTenantScopedEventStore(TenantScope.For("globex"), runId);

		await Assert.That((await other.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsFailure).IsTrue();
	}

	// An unowned row - written tenant-less, or by a write path that missed the stamp - must not become
	// visible to every tenant. Failing closed turns a stamping bug into absent data, not a leak.
	[Test]
	public async Task GetAsync_GivenAnUnownedAggregate_IsNotVisibleToATenant(CancellationToken cancellationToken)
	{
		var runId = Guid.NewGuid();
		var aggregateId = $"{Guid.NewGuid()}";

		var system = CreateTenantScopedEventStore(TenantScope.System, runId);
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();
		await system.SaveAsync(aggregate, cancellationToken: cancellationToken);

		var tenant = CreateTenantScopedEventStore(TenantScope.For("acme"), runId);

		await Assert
			.That((await tenant.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsFailure)
			.IsTrue();
		await Assert
			.That((await system.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsSuccess)
			.IsTrue();
	}

	[Test]
	public async Task SaveAsync_GivenAggregateLoadedBeforeAnotherWriterSaved_ReturnsConcurrencyConflict(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();
		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		var firstResult = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);
		var secondResult = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);

		await Assert.That(firstResult.IsSuccess).IsTrue();
		await Assert.That(secondResult.IsSuccess).IsTrue();

		var first = firstResult.Value;
		var second = secondResult.Value;

		first.IncrementInt32Value();
		second.IncrementInt32Value();

		// Act
		var firstSave = await eventStore.SaveAsync(first, cancellationToken: cancellationToken);
		var secondSave = await eventStore.SaveAsync(second, cancellationToken: cancellationToken);

		// Assert - the second writer read the same version, so its write loses. It must lose as the
		// shared ConcurrencyConflict case: a caller handles it without referencing a provider assembly.
		await Assert.That(firstSave.IsSuccess).IsTrue();
		await Assert.That(secondSave.IsFailure).IsTrue();
		await Assert.That(secondSave.Error is ConcurrencyConflict).IsTrue();
	}

	[Test]
	public async Task SaveAsync_GivenNewAggregateWithChanges_SavesAggregate(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		// Act
		var result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();
		await Assert.That(aggregate.IsNew()).IsFalse();

		var aggregateFromEventStoreResult = await eventStore.GetAsync(
			aggregateId,
			cancellationToken: cancellationToken
		);
		var aggregateFromEventStore = aggregateFromEventStoreResult.IsSuccess
			? aggregateFromEventStoreResult.Value
			: null;

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
		var result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();
		await Assert.That(result.Value.Kind).IsEqualTo(SaveOutcomeKind.NoChanges);
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
			.AfterSaveAsync(aggregate, Any<long>(), true, Any<EventRecord[]>(), Any<CancellationToken>())
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
		var result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(beforeWasCalled).IsTrue();
		await Assert.That(afterWasCalled).IsTrue();

		aggregateChangeNotifier.BeforeSaveAsync(aggregate, true, Any<CancellationToken>()).WasCalled(Times.Once);

		aggregateChangeNotifier
			.AfterSaveAsync(
				aggregate,
				Any<long>(),
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
		await Assert.That(result.IsFailure).IsTrue();
		await Assert.That(result.Error is AggregateInvalid).IsTrue();

		var invalid = result.Error is AggregateInvalid value ? value : default;

		await Assert.That(invalid.ValidationResult.Failures).HasSingleItem();
		await Assert
			.That(invalid.ValidationResult.Failures.Single().PropertyName)
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
		await Assert.That(result.IsSuccess && result.Value.Written).IsTrue();

		// Act
		var aggregateGetResultResult = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);
		var aggregateGetResult = aggregateGetResultResult.IsSuccess ? aggregateGetResultResult.Value : null;

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
		var result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();
		await Assert.That(aggregate.IsNew()).IsFalse();

		var aggregateFromEventStoreResult = await eventStore.GetAsync(
			aggregateId,
			cancellationToken: cancellationToken
		);
		var aggregateFromEventStore = aggregateFromEventStoreResult.IsSuccess
			? aggregateFromEventStoreResult.Value
			: null;

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
		var result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();
		await Assert.That(aggregate.IsNew()).IsFalse();

		// Re-read the aggregate straight from the event records, bypassing the snapshot.
		var aggregateFromEventStoreResult = await eventStore.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SkipSnapshot = true },
			cancellationToken: cancellationToken
		);
		var aggregateFromEventStore = aggregateFromEventStoreResult.IsSuccess
			? aggregateFromEventStoreResult.Value
			: null;

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
		var resultResult = await eventStore.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SkipSnapshot = true },
			cancellationToken: cancellationToken
		);
		var result = resultResult.IsSuccess ? resultResult.Value : null;

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
		var resultResult = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);
		var result = resultResult.IsSuccess ? resultResult.Value : null;

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
		var resultResult = await eventStore.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SkipSnapshot = true },
			cancellationToken: cancellationToken
		);
		var result = resultResult.IsSuccess ? resultResult.Value : null;

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
		var resultResult = await eventStore.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SkipSnapshot = true },
			cancellationToken: cancellationToken
		);
		var result = resultResult.IsSuccess ? resultResult.Value : null;

		// Assert
		await EventStoreTestSeed.AssertRecreatedWithTotals(result, aggregate, totalEvents);
	}

	[Test]
	public async Task GetAsync_GivenAggregateIsDeleted_ReportsAggregateDeleted(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await eventStore.DeleteAsync(aggregate, cancellationToken: cancellationToken);

		// Act / Assert
		await EventStoreTestSeed.AssertGetAsyncReportsDeleted(eventStore, aggregateId, cancellationToken);
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
		var resultResult = await eventStore.GetAtAsync(
			aggregateId,
			version: previousEventsToCreate,
			cancellationToken: cancellationToken
		);
		var result = resultResult.IsSuccess ? resultResult.Value : null;

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
		var resultResult = await eventStore.GetOrCreateAsync(
			aggregateId,
			operationContext: null,
			cancellationToken: cancellationToken
		);
		var result = resultResult.IsSuccess ? resultResult.Value : null;

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
		await Assert.That(result.Status).IsEqualTo(ExistenceStatus.Exists);
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
		var aggregateResultResult = await eventStore.GetDeletedAsync(aggregateId, cancellationToken: cancellationToken);
		var aggregateResult = aggregateResultResult.IsSuccess ? aggregateResultResult.Value : null;

		// Assert
		await Assert.That(aggregateResult).IsNotNull();
		await Assert.That(aggregateResult!.Details.IsDeleted).IsTrue();
		await Assert.That(aggregateResult.Details.SavedVersion).IsEqualTo(2);
	}

	[Test]
	public async Task ExistsAsync_GivenDeletedAggregate_ReportsExistsInDeletedState(CancellationToken cancellationToken)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = CreateEventStore();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await eventStore.DeleteAsync(aggregate, cancellationToken: cancellationToken);

		// Act
		var result = await eventStore.ExistsAsync(aggregateId, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.Status).IsEqualTo(ExistenceStatus.ExistsInDeletedState);
	}

	[Test]
	public async Task ExistsAsync_GivenLiveAggregate_ReportsExists(CancellationToken cancellationToken)
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
		await Assert.That(result.Status).IsEqualTo(ExistenceStatus.Exists);
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

		var aggregateResult = await eventStore.GetDeletedAsync(aggregateId, cancellationToken);
		aggregate = aggregateResult.IsSuccess ? aggregateResult.Value : null;
		await Assert.That(aggregate).IsNotNull();

		// Act
		var result = await eventStore.RestoreAsync(aggregate!, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();
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

		var aggregateResult = (await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken)).Value;

		// Act
		var result = await eventStore.DeleteAsync(aggregateResult, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();
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

		var aggregateResult = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);
		aggregate = aggregateResult.IsSuccess ? aggregateResult.Value : null;
		await Assert.That(aggregate).IsNotNull();

		// Act
		var result = await eventStore.DeleteAsync(
			aggregate!,
			new EventStoreOperationContext { PermanentlyDelete = true },
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();

		// The aggregate and all of its data are gone.
		await Assert
			.That((await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsFailure)
			.IsTrue();
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

		var aggregateResult = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);
		aggregate = aggregateResult.IsSuccess ? aggregateResult.Value : null;
		await Assert.That(aggregate).IsNotNull();

		// Act
		var result = await eventStore.DeleteAsync(
			aggregate!,
			new EventStoreOperationContext { PermanentlyDelete = true },
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();

		// The aggregate and all of its data are gone.
		await Assert
			.That((await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsFailure)
			.IsTrue();
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
