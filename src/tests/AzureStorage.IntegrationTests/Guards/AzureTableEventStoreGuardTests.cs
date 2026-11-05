using System.Diagnostics.CodeAnalysis;
using System.Text;
using Azure.Data.Tables;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Aggregates.Persistence.Events;
using Purview.EventSourcing.AzureStorage.Entities;
using Purview.EventSourcing.AzureStorage.StorageClients.Blob;
using Purview.EventSourcing.AzureStorage.StorageClients.Table;
using Purview.EventSourcing.Fixtures.AzureStorage;

namespace Purview.EventSourcing.AzureStorage.Guards;

/// <summary>
/// Azure Storage-specific event-store tests: batch limits, stream-version persistence, cache
/// eviction, telemetry observations and direct storage-layout validation. The provider-agnostic
/// behavior lives in the shared <see cref="Contracts.EventStoreContractTestsBase{TAggregate}" />.
/// </summary>
[ClassDataSource<TableEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class AzureTableEventStoreGuardTests(TableEventStoreFixture fixture)
{
	public static IEnumerable<int> HighEventCountTestData()
	{
		const int maximum = AzureTableClient.MaximumBatchSize;

		yield return maximum - 2;
		yield return maximum + (maximum / 2);
		yield return maximum * 2;
		yield return maximum * 3;
		yield return maximum * 4;
		yield return maximum * 5;
		yield return maximum * 9;
	}

	[Test]
	[MethodDataSource(nameof(HighEventCountTestData))]
	public async Task SaveAsync_GivenEventCountIsGreaterThanMaximumNumberOfAllowedInBatchOperation_BatchesEvents(
		int eventsToGenerate,
		CancellationToken cancellationToken
	)
	{
		// Minus 2 is because we also add the idempotency marker and stream on the first batch.
		if (eventsToGenerate < (AzureTableClient.MaximumBatchSize - 2))
			await Assert
				.That($"'{eventsToGenerate}' should be greater than {AzureTableClient.MaximumBatchSize}.")
				.IsNull();

		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = BuildAggregateWithIncrementEvents(aggregateId, eventsToGenerate);

		var ctx = fixture.CreateEventStoreContext<PersistenceAggregate>();
		var eventStore = ctx.EventStore;
		var tableClient = ctx.TableClient;

		// Act
		var result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		var streamVersionVersion = await GetStreamVersionNumberAndAssertNotNull(
			tableClient,
			aggregateId,
			cancellationToken
		);

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

		await AssertRecreatedMatchesSource(aggregateFromEventStore, aggregate);

		await Assert.That(streamVersionVersion).IsEqualTo(eventsToGenerate);
	}

	[Test]
	[Arguments(1)]
	[Arguments(10)]
	[Arguments(20)]
	[Arguments(50)]
	public async Task SaveAsync_GivenStreamVersionWithoutVersionSetWhenSaved_StreamVersionHasCorrectEvent(
		int eventsToGenerate,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var aggregateId = $"{Guid.NewGuid()}";
		var aggregate = BuildAggregateWithIncrementEvents(aggregateId, eventsToGenerate);

		var ctx = fixture.CreateEventStoreContext<PersistenceAggregate>();
		var eventStore = ctx.EventStore;
		var tableClient = ctx.TableClient;

		// Act
		var result = await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Get and update stream version to remove the Version property.
		var streamVersionVersion = await ReadAndRemoveStreamVersion(tableClient, aggregateId, cancellationToken);

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

		await AssertRecreatedMatchesSource(aggregateFromEventStore, aggregate);

		await Assert.That(streamVersionVersion).IsEqualTo(eventsToGenerate);
	}

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
		var tableClient = fixture.TableClient;
		var blobClient = fixture.BlobClient;

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

		await ValidateEntitiesDeletedAsync(aggregate, eventStore, tableClient, blobClient, cancellationToken);
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
		var tableClient = fixture.TableClient;
		var blobClient = fixture.BlobClient;

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

		await ValidateEntitiesDeletedAsync(aggregate, eventStore, tableClient, blobClient, cancellationToken);
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
		var tableClient = ctx.TableClient;

		// Act
		await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Update existing events to make them unknown types effectively.
		await MarkEventsAsUnknown(
			eventStore,
			tableClient,
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

	static PersistenceAggregate BuildAggregateWithIncrementEvents(string aggregateId, int eventCount)
	{
		var aggregate = TestHelpers.Aggregate<PersistenceAggregate>(aggregateId: aggregateId);

		for (var i = 0; i < eventCount; i++)
			aggregate.IncrementInt32Value();

		return aggregate;
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
		TableEventStore<PersistenceAggregate> eventStore,
		AzureTableClient tableClient,
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

			batch.Update(eventToUpdate, merge: false);
		}

		await tableClient.SubmitBatchAsync(batch, cancellationToken);
	}

	static async Task<long?> ReadAndRemoveStreamVersion(
		AzureTableClient tableClient,
		string aggregateId,
		CancellationToken cancellationToken
	)
	{
		// Get and update stream version to remove the Version property.
		var streamVersion =
			await tableClient.GetAsync<TableEntity>(
				aggregateId,
				TableEventStoreConstants.StreamVersionRowKey,
				cancellationToken: cancellationToken
			) ?? throw new NullReferenceException();
		// Stored as Edm.Int64 since the stream version widened; reading it as int? would silently yield null.
		var streamVersionVersion = streamVersion[nameof(StreamVersionEntity.Version)] as long?;

		streamVersion.Remove(nameof(StreamVersionEntity.Version));

		await tableClient.OperationAsync(
			TableTransactionActionType.UpdateReplace,
			streamVersion,
			cancellationToken: cancellationToken
		);

		return streamVersionVersion;
	}

	static async Task<long?> GetStreamVersionNumberAndAssertNotNull(
		AzureTableClient tableClient,
		string aggregateId,
		CancellationToken cancellationToken
	)
	{
		// Get and update stream version to remove the Version property.
		var streamVersion = await tableClient.GetAsync<TableEntity>(
			aggregateId,
			TableEventStoreConstants.StreamVersionRowKey,
			cancellationToken: cancellationToken
		);

		await Assert.That(streamVersion).IsNotNull();

		// Stored as Edm.Int64 since the stream version widened; reading it as int? would silently yield null.
		return streamVersion![nameof(StreamVersionEntity.Version)] as long?;
	}

	static async Task AssertRecreatedMatchesSource(PersistenceAggregate? result, PersistenceAggregate aggregate)
	{
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Id()).IsEqualTo(aggregate.Id());
		await Assert.That(result.IncrementInt32).IsEqualTo(aggregate.IncrementInt32);
		await Assert.That(result.Details.SavedVersion).IsEqualTo(aggregate.Details.SavedVersion);
		await Assert.That(result.Details.CurrentVersion).IsEqualTo(aggregate.Details.CurrentVersion);
		await Assert.That(result.Details.SnapshotVersion).IsEqualTo(aggregate.Details.SnapshotVersion);
	}

	static async Task ValidateEntitiesDeletedAsync(
		PersistenceAggregate aggregate,
		TableEventStore<PersistenceAggregate> eventStore,
		AzureTableClient tableClient,
		AzureBlobClient blobClient,
		CancellationToken cancellationToken
	)
	{
		var results = await tableClient.QueryAsync<TableEntity>(
			m => m.PartitionKey == aggregate.Details.Id,
			cancellationToken: cancellationToken
		);

		await Assert.That(results.Results).IsEmpty();

		var prefix = eventStore.GenerateSnapshotBlobPath(aggregate.Id());
		var blobResults = await blobClient.GetBlobsAsync(prefix, cancellationToken: cancellationToken);
		var blobsToDelete = blobResults.ToBlockingEnumerable(cancellationToken: cancellationToken);

		await Assert.That(blobsToDelete).IsEmpty();
	}
}
