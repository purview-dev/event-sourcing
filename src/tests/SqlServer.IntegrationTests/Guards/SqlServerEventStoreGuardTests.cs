using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Aggregates.Persistence.Events;
using Purview.EventSourcing.Fixtures.SqlServer;

namespace Purview.EventSourcing.SqlServer.Guards;

/// <summary>
/// SQL Server-specific event-store tests: cache eviction, telemetry observations and direct
/// existence checks. The provider-agnostic behavior lives in the shared
/// <see cref="Contracts.EventStoreContractTestsBase{TAggregate}" />.
/// </summary>
[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class SqlServerEventStoreGuardTests(SqlServerEventStoreFixture fixture)
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

		var aggregateResult =
			await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken)
			?? throw new NullReferenceException();

		// Act
		var result = await eventStore.DeleteAsync(aggregateResult, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result).IsTrue();

		cache.RemoveAsync(cacheKey, Any<CancellationToken>()).WasCalled(Times.Once);
	}

	[Test]
	public async Task DeleteAsync_GivenAggregateExists_PermanentlyDeletesAllData_DoesNotExist(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = TestHelpers.Aggregate<PersistenceAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var eventStore = fixture.CreateEventStore<PersistenceAggregate>();

		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Act
		var result = await eventStore.DeleteAsync(
			aggregate,
			new EventStoreOperationContext { PermanentlyDelete = true },
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsTrue();
		await Assert.That(aggregate.Details.IsDeleted).IsTrue();
		await Assert.That(aggregate.Details.Locked).IsTrue();

		// Verify all data was removed.
		var exists = await eventStore.ExistsAsync(aggregateId, cancellationToken: cancellationToken);
		await Assert.That(exists.Status).IsEqualTo(ExistsStatus.DoesNotExist);
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
		bool result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result).IsFalse();

		telemetry.SaveContainedNoChanges(aggregateId, Any<string>(), Any<string>()).WasCalled(Times.Once);
	}

	[Test]
	[MethodDataSource(nameof(OldEventCountTestData))]
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

		var result = await eventStore.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SkipSnapshot = true },
			cancellationToken: cancellationToken
		);

		// Assert
		telemetry
			.CannotApplyEvent(
				aggregateId,
				Any<string>(),
				Any<string>(),
				Any<string>(),
				Is<string>(eventType => eventType!.Contains(nameof(OldEvent), StringComparison.Ordinal)),
				Any<int>()
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
		var client = ctx.Client;

		// Act
		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Update existing events to make them unknown types effectively.
		await foreach (
			var row in client.GetEventRangeAsync(
				aggregateId,
				aggregate.AggregateType,
				eventsToCreate + 1,
				totalEvents,
				cancellationToken
			)
		)
		{
			await client.UpsertAsync(
				row.Id,
				row.EntityType,
				row.AggregateId,
				row.AggregateType,
				row.Version,
				row.IsDeleted,
				row.Payload,
				unknownEventType,
				row.IdempotencyId,
				row.Timestamp,
				cancellationToken
			);
		}

		var result = await eventStore.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SkipSnapshot = true },
			cancellationToken: cancellationToken
		);

		// Assert
		telemetry
			.SkippedUnknownEvent(aggregateId, Any<string>(), Any<string>(), unknownEventType, Any<int>())
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
}
