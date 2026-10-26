using System.Diagnostics.CodeAnalysis;
using System.Text;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Aggregates.Persistence.Events;
using Purview.EventSourcing.Fixtures.MongoDB;
using Purview.EventSourcing.MongoDB.Events.Entities;
using Purview.EventSourcing.MongoDB.StorageClient;

namespace Purview.EventSourcing.MongoDB.Guards;

/// <summary>
/// MongoDB-specific event-store tests: cache eviction, telemetry observations and direct
/// storage-layout validation. The provider-agnostic behavior lives in the shared
/// <see cref="Contracts.EventStoreContractTestsBase{TAggregate}" />.
/// </summary>
[ClassDataSource<MongoDBEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class MongoDBEventStoreGuardTests(MongoDBEventStoreFixture fixture)
{
	[Test]
	public async Task DeleteAsync_WhenTableStoreConfigRemoveDeletedFromCacheIsTrueAndPreviouslySavedAggregate_RemovesFromCache(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<PersistenceAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var ctx = fixture.CreateEventStoreContext<PersistenceAggregate>(removeFromCacheOnDelete: true);
		var eventStore = ctx.EventStore;
		var cache = ctx.Cache;

		var cacheKey = eventStore.CreateCacheKey(aggregateId);

		await eventStore.SaveAsync(aggregate, cancellationToken);

		var aggregateResult = (await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken)).Value;

		// Act
		var result = await eventStore.DeleteAsync(aggregateResult, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();

		cache.RemoveAsync(cacheKey, Any<CancellationToken>()).WasCalled(Times.Once);
	}

	[Test]
	public async Task DeleteAsync_GivenAggregateExists_PermanentlyDeletesAllData_ValidatesStorageCleared(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<PersistenceAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = fixture.CreateEventStore<PersistenceAggregate>();
		var eventClient = fixture.EventClient;
		var snapshotClient = fixture.SnapshotClient;

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
		await Assert.That(aggregate.Details.IsDeleted).IsTrue();
		await Assert.That(aggregate.Details.Locked).IsTrue();

		await ValidateEntitiesDeletedAsync(aggregate, eventClient, snapshotClient, cancellationToken);
	}

	[Test]
	public async Task DeleteAsync_GivenAggregateExistsWithLargeEvent_PermanentlyDeletesAllData_ValidatesStorageCleared(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<PersistenceAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var value = string.Empty;
		var sizeIsLessThan32K = true;
		while (sizeIsLessThan32K)
		{
			value += "abcdefghijklmnopqrstvwxyz";
			value += "ABCDEFGHIJKLMNOPQRSTVWXYZ";
			value += "1234567890";

			sizeIsLessThan32K = Encoding.UTF8.GetByteCount(value) < short.MaxValue;
		}

		aggregate.AppendString(value);

		var eventStore = fixture.CreateEventStore<PersistenceAggregate>();
		var eventClient = fixture.EventClient;
		var snapshotClient = fixture.SnapshotClient;

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
		await Assert.That(aggregate.Details.IsDeleted).IsTrue();
		await Assert.That(aggregate.Details.Locked).IsTrue();

		await ValidateEntitiesDeletedAsync(aggregate, eventClient, snapshotClient, cancellationToken);
	}

	[Test]
	public async Task SaveAsync_GivenAggregateWithNoChanges_TelemetryLogsSaveContainedNoChanges(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<PersistenceAggregate>(aggregateId: aggregateId);

		var ctx = fixture.CreateEventStoreContext<PersistenceAggregate>();
		var eventStore = ctx.EventStore;
		var telemetry = ctx.Telemetry;

		// Act
		var result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();
		await Assert.That(result.Value.Kind).IsEqualTo(SaveOutcomeKind.NoChanges);

		telemetry.SaveContainedNoChanges(aggregateId, Any<string>(), Any<string>()).WasCalled(Times.Once);
	}

	[Test]
	[MethodDataSource(nameof(OldEventCountTestData))]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "Guard scenario intentionally touches the full event-store surface."
	)]
	public async Task GetAsync_GivenAnAggregateWithNonRegisteredEventType_TelemetryLogsCannotApplyEvent(
		int eventsToCreate,
		int numberOfOldEventsToCreate,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var totalEvents = eventsToCreate + numberOfOldEventsToCreate;

		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = BuildAggregateWithOldEvents(aggregateId, eventsToCreate, numberOfOldEventsToCreate);

		var ctx = fixture.CreateEventStoreContext<PersistenceAggregate>();
		var eventStore = ctx.EventStore;
		var telemetry = ctx.Telemetry;

		// Act
		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		var resultResult = await eventStore.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SkipSnapshot = true },
			cancellationToken: cancellationToken
		);
		var result = resultResult.IsSuccess ? resultResult.Value : null;

		// Assert
		telemetry
			.CannotApplyEvent(
				aggregateId,
				Any<string>(),
				Any<string>(),
				Any<string>(),
				Is<string>(eventType => eventType!.Contains(nameof(OldEvent), StringComparison.Ordinal)),
				Any<long>()
			)
			.WasCalled(Times.Exactly(numberOfOldEventsToCreate));

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Details.SavedVersion).IsEqualTo(totalEvents);
	}

	[Test]
	[MethodDataSource(nameof(OldEventCountTestData))]
	public async Task GetAsync_GivenAnAggregateWithUnknownEventType_TelemetryLogsSkippedUnknownEvent(
		int eventsToCreate,
		int numberOfOldEventsToCreate,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string unknownEventType = "an-unknown-type";

		var totalEvents = eventsToCreate + numberOfOldEventsToCreate;

		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = BuildAggregateWithOldEvents(aggregateId, eventsToCreate, numberOfOldEventsToCreate);

		var ctx = fixture.CreateEventStoreContext<PersistenceAggregate>();
		var eventStore = ctx.EventStore;
		var telemetry = ctx.Telemetry;
		var eventClient = ctx.EventClient;

		// Act
		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Update existing events to make them unknown types effectively.
		await MarkEventsAsUnknown(
			eventStore,
			eventClient,
			aggregateId,
			eventsToCreate + 1,
			totalEvents,
			unknownEventType,
			cancellationToken
		);

		var resultResult = await eventStore.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SkipSnapshot = true },
			cancellationToken: cancellationToken
		);
		var result = resultResult.IsSuccess ? resultResult.Value : null;

		// Assert
		telemetry
			.SkippedUnknownEvent(aggregateId, Any<string>(), Any<string>(), unknownEventType, Any<long>())
			.WasCalled(Times.Exactly(numberOfOldEventsToCreate));

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Details.SavedVersion).IsEqualTo(totalEvents);
	}

	public static IEnumerable<(int, int)> OldEventCountTestData()
	{
		yield return (1, 1);
		yield return (5, 2);
		yield return (10, 5);
		yield return (20, 20);
	}

	static PersistenceAggregate BuildAggregateWithOldEvents(
		string aggregateId,
		int eventsToCreate,
		int numberOfOldEventsToCreate
	)
	{
		var aggregate = TestHelpers.Aggregate<PersistenceAggregate>(aggregateId: aggregateId);
		// Register the event type here...!
		aggregate.RegisterOldEventType();

		for (var i = 0; i < eventsToCreate; i++)
			aggregate.IncrementInt32Value();

		for (var i = 0; i < numberOfOldEventsToCreate; i++)
			aggregate.SetOldEventValue(Guid.NewGuid());

		return aggregate;
	}

	static async Task MarkEventsAsUnknown(
		MongoDBEventStore<PersistenceAggregate> eventStore,
		MongoDBClient eventClient,
		string aggregateId,
		int fromVersion,
		int toVersion,
		string unknownEventType,
		CancellationToken cancellationToken
	)
	{
		// Update existing events to make them unknown types effectively.
		var eventsToUpdate = eventStore.GetEventRangeEntitiesAsync(
			aggregateId,
			fromVersion,
			toVersion,
			cancellationToken
		);

		BatchOperation batchOperation = new();
		var batch = batchOperation;
		await foreach (var eventToUpdate in eventsToUpdate)
		{
			eventToUpdate.EventType = unknownEventType;

			batch.Update(eventToUpdate);
		}

		await eventClient.SubmitBatchAsync(batch, cancellationToken);
	}

	static async Task ValidateEntitiesDeletedAsync(
		PersistenceAggregate aggregate,
		MongoDBClient eventClient,
		MongoDBClient snapshotClient,
		CancellationToken cancellationToken
	)
	{
		var eventCount = await eventClient.CountAsync<EventEntity>(
			m => m.AggregateId == aggregate.Id() && m.EntityType == EntityTypes.EventType,
			cancellationToken: cancellationToken
		);
		await Assert.That(eventCount).IsEqualTo(0);

		var streamVersionCount = await eventClient.CountAsync<StreamVersionEntity>(
			m => m.AggregateId == aggregate.Id() && m.EntityType == EntityTypes.StreamVersionType,
			cancellationToken: cancellationToken
		);
		await Assert.That(streamVersionCount).IsEqualTo(0);

		var idempotencyMarkerCount = await eventClient.CountAsync<IdempotencyMarkerEntity>(
			m => m.AggregateId == aggregate.Id() && m.EntityType == EntityTypes.StreamVersionType,
			cancellationToken: cancellationToken
		);
		await Assert.That(idempotencyMarkerCount).IsEqualTo(0);

		var snapshotEntity = await snapshotClient.GetAsync<SnapshotEntity>(
			m => m.Id == aggregate.Id() && m.EntityType == EntityTypes.SnapshotType,
			cancellationToken: cancellationToken
		);
		await Assert.That(snapshotEntity).IsNull();
	}
}
