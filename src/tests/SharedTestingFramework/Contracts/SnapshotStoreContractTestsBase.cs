using System.Linq.Expressions;
using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Contracts;

/// <summary>
/// Provider-agnostic snapshot-store contract tests.
///
/// This file is linked (not compiled) into each provider integration test project. A provider
/// wires it up with a small generic class that combines <c>[GenerateGenericTest]</c>,
/// <c>[ClassDataSource&lt;TF&gt;]</c> and <c>[InheritsTests]</c> against its own fixture.
///
/// Tests only exercise the public queryable event-store contract (<see cref="IQueryableEventStoreCore{T}"/>)
/// plus snapshotting. Provider-internal behavior (index creation, JSON operators, translation boundaries,
/// storage layout) belongs in per-provider guard tests.
/// </summary>
public abstract class SnapshotStoreContractTestsBase<TAggregate>
	where TAggregate : class, IAggregateTest, new()
{
	protected IQueryableEventStoreCore<TAggregate> Store
	{
		get => field ??= CreateSnapshotStore();
		private set;
	}

	protected abstract IQueryableEventStoreCore<TAggregate> CreateSnapshotStore();

	protected abstract Task SnapshotAsync(
		IQueryableEventStoreCore<TAggregate> store,
		TAggregate aggregate,
		CancellationToken cancellationToken = default
	);

	protected static TAggregate CreateAggregate(string? id = null, Action<TAggregate>? action = null)
	{
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: id ?? $"{Guid.NewGuid()}");

		action?.Invoke(aggregate);

		return aggregate;
	}

	static EventStoreOperationContext CreateOperationContext() => new() { CorrelationId = $"{Guid.NewGuid()}" };
#pragma warning restore CA1506

	[Test]
	public async Task SaveAsync_GivenNewAggregateWithChanges_SavesAggregate(CancellationToken cancellationToken)
	{
		// Arrange
		var store = Store;

		var aggregateId = Guid.NewGuid().ToString();
		var aggregate = CreateAggregate(id: aggregateId);
		aggregate.IncrementInt32Value();
		aggregate.AppendString(aggregateId);

		// Act
		var result = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();
		await Assert.That(aggregate.IsNew()).IsFalse();

		// Verify by re-getting the aggregate, knowing that the cache is disabled.
		var aggregateFromStoreResult = await store.GetAsync(aggregateId, cancellationToken: cancellationToken);
		var aggregateFromStore = aggregateFromStoreResult.IsSuccess ? aggregateFromStoreResult.Value : null;

		await Assert.That(aggregateFromStore).IsNotNull();
		await Assert.That(aggregateFromStore!.Id()).IsEqualTo(aggregate.Id());
		await Assert.That(aggregateFromStore.IncrementInt32).IsEqualTo(aggregate.IncrementInt32);
		await Assert.That(aggregateFromStore.StringProperty).IsEqualTo(aggregateId);
		await Assert.That(aggregateFromStore.Details.SavedVersion).IsEqualTo(aggregate.Details.SavedVersion);
		await Assert.That(aggregateFromStore.Details.CurrentVersion).IsEqualTo(aggregate.Details.CurrentVersion);
		await Assert.That(aggregateFromStore.Details.SnapshotVersion).IsEqualTo(aggregate.Details.SnapshotVersion);
		await Assert.That(aggregateFromStore.Details.Etag).IsEqualTo(aggregate.Details.Etag);
	}

	[Test]
	public async Task SnapshotAsync_GivenNewAggregateWithChanges_PersistsSnapshot(CancellationToken cancellationToken)
	{
		// Arrange
		var store = Store;

		var aggregateId = Guid.NewGuid().ToString();
		var aggregate = CreateAggregate(id: aggregateId);
		aggregate.IncrementInt32Value();
		aggregate.SetInt32Value(42);

		// Act
		await SnapshotAsync(store, aggregate, cancellationToken);

		// Assert
		// A bare SnapshotAsync writes only the queryable snapshot (no events), so it is observed
		// through the snapshot-query path rather than GetAsync, which is event-store-backed.
		var fromStore = await store.SingleOrDefaultAsync(m => m.Int32Value == 42, cancellationToken: cancellationToken);

		await Assert.That(fromStore).IsNotNull();
		await Assert.That(fromStore!.Id()).IsEqualTo(aggregateId);
		await Assert.That(fromStore.IncrementInt32).IsEqualTo(1);
		await Assert.That(fromStore.Int32Value).IsEqualTo(42);
		await Assert.That(fromStore.Details.IsDeleted).IsFalse();
	}

	[Test]
	public async Task SaveAsync_GivenAggregateWithComplexProperties_PersistsCorrectly(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var store = Store;

		var aggregateId = Guid.NewGuid().ToString();
		var aggregate = CreateAggregate(id: aggregateId);

		aggregate.IncrementInt32Value();
		aggregate.IncrementInt32Value();
		aggregate.IncrementInt32Value();
		aggregate.SetInt32Value(42);
		aggregate.AppendString("hello-");
		aggregate.AppendString("world");
		aggregate.SetComplexProperty(
			new ComplexTestType
			{
				Int16Property = 16,
				Int32Property = 32,
				Int64Property = 64,
				StringProperty = "complex-test",
				DateTimeOffsetProperty = DateTimeOffset.UtcNow,
			}
		);

		// Act
		var saveResult = await store.SaveAsync(
			aggregate,
			CreateOperationContext(),
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(saveResult.IsSuccess).IsTrue();

		var fromDbResult = await store.GetAsync(aggregateId, cancellationToken: cancellationToken);
		var fromDb = fromDbResult.IsSuccess ? fromDbResult.Value : null;

		await Assert.That(fromDb).IsNotNull();
		await Assert.That(fromDb.IncrementInt32).IsEqualTo(3);
		await Assert.That(fromDb.Int32Value).IsEqualTo(42);
		await Assert.That(fromDb.StringProperty).IsEqualTo("hello-world");
		await Assert.That(fromDb.ComplexTestType).IsNotNull();
		await Assert.That(fromDb.ComplexTestType!.Int16Property).IsEqualTo((short)16);
		await Assert.That(fromDb.ComplexTestType.Int32Property).IsEqualTo(32);
		await Assert.That(fromDb.ComplexTestType.Int64Property).IsEqualTo(64);
		await Assert.That(fromDb.ComplexTestType.StringProperty).IsEqualTo("complex-test");

		// Also verify via LINQ query.
		var queried = await store.SingleOrDefaultAsync(m => m.Int32Value == 42, cancellationToken: cancellationToken);
		await Assert.That(queried).IsNotNull();
		await Assert.That(queried!.Id()).IsEqualTo(aggregateId);
		await Assert.That(queried.ComplexTestType).IsNotNull();
		await Assert.That(queried.ComplexTestType!.StringProperty).IsEqualTo("complex-test");
	}

	[Test]
	public async Task SaveAsync_GivenMultipleSavesOfSameAggregate_UpdatesSnapshot(CancellationToken cancellationToken)
	{
		// Arrange
		var store = Store;

		var aggregateId = Guid.NewGuid().ToString();
		var aggregate = CreateAggregate(id: aggregateId);
		aggregate.IncrementInt32Value();

		var firstSave = await store.SaveAsync(
			aggregate,
			CreateOperationContext(),
			cancellationToken: cancellationToken
		);
		await Assert.That(firstSave.IsSuccess).IsTrue();

		var firstVersion = aggregate.Details.CurrentVersion;

		// Modify and save again.
		aggregate.IncrementInt32Value();
		aggregate.SetInt32Value(99);

		var secondSave = await store.SaveAsync(
			aggregate,
			CreateOperationContext(),
			cancellationToken: cancellationToken
		);
		await Assert.That(secondSave.IsSuccess).IsTrue();

		// Act - read from the store.
		var fromDbResult = await store.GetAsync(aggregateId, cancellationToken: cancellationToken);
		var fromDb = fromDbResult.IsSuccess ? fromDbResult.Value : null;

		// Assert - should have the latest state.
		await Assert.That(fromDb).IsNotNull();
		await Assert.That(fromDb.IncrementInt32).IsEqualTo(2);
		await Assert.That(fromDb.Int32Value).IsEqualTo(99);
		await Assert.That(fromDb.Details.CurrentVersion).IsGreaterThan(firstVersion);
	}

	[Test]
	public async Task DeleteAsync_GivenExistingAggregateMarkedAsDeleted_DeletesFromStore(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var store = Store;

		var aggregateId = Guid.NewGuid().ToString();
		var aggregate = CreateAggregate(id: aggregateId);
		aggregate.IncrementInt32Value();

		var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await Assert.That(saveResult.IsSuccess).IsTrue();

		var aggregateFromStoreResult = await store.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SnapshotCacheMode = SnapshotCachingOptions.None },
			cancellationToken: cancellationToken
		);
		var aggregateFromStore = aggregateFromStoreResult.IsSuccess ? aggregateFromStoreResult.Value : null;
		await Assert.That(aggregateFromStore).IsNotNull();

		// Act
		var deleteResult = await store.DeleteAsync(aggregate, cancellationToken: cancellationToken);

		aggregateFromStoreResult = await store.GetAsync(
			aggregateId,
			new EventStoreOperationContext { SnapshotCacheMode = SnapshotCachingOptions.None },
			cancellationToken: cancellationToken
		);
		aggregateFromStore = aggregateFromStoreResult.IsSuccess ? aggregateFromStoreResult.Value : null;

		// Assert
		await Assert.That(deleteResult.IsSuccess).IsTrue();
		await Assert.That(aggregateFromStore).IsNull();
	}

	[Test]
	public async Task RestoreAsync_GivenDeletedAggregate_RestoresSnapshot(CancellationToken cancellationToken)
	{
		// Arrange
		var store = Store;

		var aggregateId = Guid.NewGuid().ToString();
		var aggregate = CreateAggregate(id: aggregateId);
		aggregate.IncrementInt32Value();

		var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await Assert.That(saveResult.IsSuccess).IsTrue();

		var aggregateFromStoreResult = await store.GetAsync(aggregateId, cancellationToken: cancellationToken);
		var aggregateFromStore = aggregateFromStoreResult.IsSuccess ? aggregateFromStoreResult.Value : null;
		await Assert.That(aggregateFromStore).IsNotNull();

		var deleteResult = await store.DeleteAsync(aggregate, cancellationToken: cancellationToken);
		await Assert.That(deleteResult.IsSuccess).IsTrue();

		aggregateFromStoreResult = await store.GetAsync(aggregateId, cancellationToken: cancellationToken);
		aggregateFromStore = aggregateFromStoreResult.IsSuccess ? aggregateFromStoreResult.Value : null;
		await Assert.That(aggregateFromStore).IsNull();

		// Act
		var restoreResult = await store.RestoreAsync(aggregate, cancellationToken: cancellationToken);

		aggregateFromStoreResult = await store.GetAsync(aggregateId, cancellationToken: cancellationToken);
		aggregateFromStore = aggregateFromStoreResult.IsSuccess ? aggregateFromStoreResult.Value : null;

		// Assert
		await Assert.That(restoreResult.IsSuccess).IsTrue();
		await Assert.That(aggregateFromStore).IsNotNull();
	}

	[Test]
	[MethodDataSource(
		typeof(SnapshotStoreContractTestData),
		nameof(SnapshotStoreContractTestData.AggregateAndEventCounts)
	)]
	public async Task QueryAsync_GivenAggregatesExist_QueriesAsExpected(
		int numberOfAggregates,
		int numberOfEvents,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var store = Store;

		for (var aggregateIndex = 0; aggregateIndex < numberOfAggregates; aggregateIndex++)
		{
			var aggregate = CreateAggregate($"agg_{aggregateIndex}");

			for (var eventIndex = 0; eventIndex < numberOfEvents; eventIndex++)
				aggregate.IncrementInt32Value();

			var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		var aggregates = (
			await store.QueryAsync(m => m.IncrementInt32 == numberOfEvents, cancellationToken: cancellationToken)
		).Results;

		// Assert
		await Assert.That(aggregates.Length).IsEqualTo(numberOfAggregates);
	}

	[Test]
	[MethodDataSource(typeof(SnapshotStoreContractTestData), nameof(SnapshotStoreContractTestData.AggregateCounts))]
	public async Task QueryAsync_GivenAggregateType_QueriesAsExpected(
		int numberOfAggregates,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var store = Store;

		var aggregateType = CreateAggregate().AggregateType;

		for (var aggregateIndex = 0; aggregateIndex < numberOfAggregates; aggregateIndex++)
		{
			var aggregate = CreateAggregate($"agg_{aggregateIndex}");
			aggregate.IncrementInt32Value();

			var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		var aggregates = (
			await store.QueryAsync(
				m => m.AggregateType == aggregateType,
				maxRecordCount: numberOfAggregates + 1,
				cancellationToken: cancellationToken
			)
		).Results;

		// Assert
		await Assert.That(aggregates.Length).IsEqualTo(numberOfAggregates);
	}

	[Test]
	[MethodDataSource(typeof(SnapshotStoreContractTestData), nameof(SnapshotStoreContractTestData.PageSizeData))]
	public async Task ListAsync_GivenData_ListsAsExpected(
		int numberOfAggregates,
		int pageCount,
		CancellationToken cancellationToken
	)
	{
		const int numberOfEvents = 10;

		// Arrange
		var store = Store;
		for (var aggregateIndex = 0; aggregateIndex < numberOfAggregates; aggregateIndex++)
		{
			var aggregate = CreateAggregate($"agg_{aggregateIndex}");

			for (var eventIndex = 0; eventIndex < numberOfEvents; eventIndex++)
				aggregate.IncrementInt32Value();

			var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		List<TAggregate> aggregates = [];

		var aggregateResponse = await store.ListAsync(maxRecordCount: pageCount, cancellationToken: cancellationToken);
		aggregates.AddRange(aggregateResponse.Results);

		while (aggregateResponse.ContinuationToken != null)
		{
			aggregateResponse = await store.ListAsync(
				aggregateResponse.ToRequest(),
				cancellationToken: cancellationToken
			);
			aggregates.AddRange(aggregateResponse.Results);
		}

		// Assert
		await Assert.That(aggregates.Count).IsEqualTo(numberOfAggregates);
	}

	[Test]
	[MethodDataSource(typeof(SnapshotStoreContractTestData), nameof(SnapshotStoreContractTestData.PageSizeData))]
	public async Task QueryAsync_GivenWhereClause_QueryAsExpected(
		int numberOfAggregates,
		int pageCount,
		CancellationToken cancellationToken
	)
	{
		const int numberOfEvents = 10;

		// Arrange
		var store = Store;

		// These are matching.
		for (var aggregateIndex = 0; aggregateIndex < numberOfAggregates; aggregateIndex++)
		{
			var aggregate = CreateAggregate($"agg_{aggregateIndex}");
			for (var eventIndex = 0; eventIndex < numberOfEvents; eventIndex++)
				aggregate.IncrementInt32Value();

			var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// These are non-matching.
		for (var aggregateIndex = 0; aggregateIndex < numberOfAggregates; aggregateIndex++)
		{
			var aggregate = CreateAggregate($"agg_{aggregateIndex + numberOfAggregates + 100000}");

			// We're changing the event count so as to make the query not match these updated records.
			for (var eventIndex = 0; eventIndex < (numberOfEvents * 2); eventIndex++)
				aggregate.IncrementInt32Value();

			var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		List<TAggregate> aggregates = [];

		Expression<Func<TAggregate, bool>> query = a => a.IncrementInt32 == numberOfEvents;

		var aggregateResponse = await store.QueryAsync(
			query,
			maxRecordCount: pageCount,
			cancellationToken: cancellationToken
		);
		aggregates.AddRange(aggregateResponse.Results);

		while (aggregateResponse.ContinuationToken != null)
		{
			aggregateResponse = await store.QueryAsync(
				query,
				aggregateResponse.ToRequest(),
				cancellationToken: cancellationToken
			);
			aggregates.AddRange(aggregateResponse.Results);
		}

		// Assert
		await Assert.That(aggregates.Count).IsEqualTo(numberOfAggregates);
	}

	[Test]
	public async Task SingleOrDefaultAsync_GivenMultipleMatchingAggregates_ThrowsException(
		CancellationToken cancellationToken
	)
	{
		const int matchingIncrement = 10;

		// Arrange
		var store = Store;

		for (var i = 0; i < matchingIncrement; i++)
		{
			var aggregate = CreateAggregate();
			for (var x = 0; x < matchingIncrement; x++)
				aggregate.IncrementInt32Value();

			var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
			await Assert.That(saveResult.IsSuccess && saveResult.Value.Written).IsTrue();
		}

		// Act
		async Task Func() =>
			await store.SingleOrDefaultAsync(
				m => m.IncrementInt32 == matchingIncrement,
				cancellationToken: cancellationToken
			);

		// Assert
		await Assert
			.That(Func)
			.Throws<InvalidOperationException>()
			.WithMessage("Sequence contains more than one element", StringComparison.Ordinal);
	}

	[Test]
	public async Task SingleOrDefaultAsync_GivenSingleMatchingAggregate_ReturnsAggregate(
		CancellationToken cancellationToken
	)
	{
		const int matchingIncrement = 10;

		// Arrange
		var store = Store;

		var aggregateId = Guid.NewGuid().ToString();
		var aggregate = CreateAggregate(id: aggregateId);
		for (var x = 0; x < matchingIncrement; x++)
			aggregate.IncrementInt32Value();

		var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await Assert.That(saveResult.IsSuccess).IsTrue();

		// Act
		var result = await store.SingleOrDefaultAsync(
			m => m.IncrementInt32 == matchingIncrement,
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result?.Id()).IsEqualTo(aggregateId);
	}

	[Test]
	public async Task SingleOrDefaultAsync_GivenNoMatchingAggregates_ReturnsNull(CancellationToken cancellationToken)
	{
		const int aggregatesToCreate = 10;
		const int eventsToCreate = 10;

		// Arrange
		var store = Store;
		for (var i = 0; i < aggregatesToCreate; i++)
		{
			var aggregate = CreateAggregate();
			for (var x = 0; x < eventsToCreate; x++)
				aggregate.IncrementInt32Value();

			var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);
			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		var result = await store.SingleOrDefaultAsync(
			m => m.IncrementInt32 == -1,
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsNull();
	}

	[Test]
	public async Task FirstOrDefaultAsync_GivenMultipleMatchingAggregatesHonoursDescendingOrder_ReturnsCorrectAggregate(
		CancellationToken cancellationToken
	)
	{
		const int aggregateCount = 10;
		const int matchingIncrement = 10;

		// Arrange
		var store = Store;

		for (var i = 0; i < aggregateCount; i++)
		{
			var aggregate = CreateAggregate();
			for (var x = 0; x < matchingIncrement; x++)
				aggregate.IncrementInt32Value();

			aggregate.SetInt32Value(i + 1);

			var saveResult = await store.SaveAsync(
				aggregate,
				CreateOperationContext(),
				cancellationToken: cancellationToken
			);

			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		var result = await store.FirstOrDefaultAsync(
			m => m.IncrementInt32 == matchingIncrement,
			orderByClause: m => m.OrderByDescending(p => p.Int32Value),
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Int32Value).IsEqualTo(aggregateCount);
	}

	[Test]
	public async Task FirstOrDefaultAsync_GivenMultipleMatchingAggregatesHonoursAscendingOrder_ReturnsCorrectAggregate(
		CancellationToken cancellationToken
	)
	{
		const int aggregateCount = 10;
		const int matchingIncrement = 10;

		// Arrange
		var store = Store;

		for (var i = 0; i < aggregateCount; i++)
		{
			var aggregate = CreateAggregate();
			for (var x = 0; x < matchingIncrement; x++)
				aggregate.IncrementInt32Value();

			aggregate.SetInt32Value(i + 1);

			var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		var result = await store.FirstOrDefaultAsync(
			m => m.IncrementInt32 == matchingIncrement,
			orderByClause: m => m.OrderBy(p => p.Int32Value),
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsNotNull();
		await Assert.That(result.Int32Value).IsEqualTo(1);
	}

	[Test]
	public async Task FirstOrDefaultAsync_GivenMultipleMatchingAggregates_ShouldNotThrowException(
		CancellationToken cancellationToken
	)
	{
		const int aggregateCount = 10;
		const int matchingIncrement = 10;

		// Arrange
		var store = Store;

		for (var i = 0; i < aggregateCount; i++)
		{
			var aggregate = CreateAggregate();
			for (var x = 0; x < matchingIncrement; x++)
			{
				aggregate.IncrementInt32Value();
			}

			var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		async Task Func() =>
			await store.FirstOrDefaultAsync(
				m => m.IncrementInt32 == matchingIncrement,
				cancellationToken: cancellationToken
			);

		// Assert
		await Assert.That(Func).ThrowsNothing();
	}

	[Test]
	public async Task FirstOrDefaultAsync_GivenMultipleMatchingAggregates_ShouldNotReturnNull(
		CancellationToken cancellationToken
	)
	{
		const int matchingIncrement = 10;

		// Arrange
		var store = Store;

		for (var i = 0; i < 10; i++)
		{
			var aggregate = CreateAggregate();
			for (var x = 0; x < matchingIncrement; x++)
				aggregate.IncrementInt32Value();

			var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		var result = await store.FirstOrDefaultAsync(
			m => m.IncrementInt32 == matchingIncrement,
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async Task FirstOrDefaultAsync_GivenSingleMatchingAggregate_ReturnsAggregate(
		CancellationToken cancellationToken
	)
	{
		const int matchingIncrement = 10;

		// Arrange
		var store = Store;

		var aggregateId = Guid.NewGuid().ToString();
		var aggregate = CreateAggregate(id: aggregateId);
		for (var x = 0; x < matchingIncrement; x++)
			aggregate.IncrementInt32Value();

		var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

		await Assert.That(saveResult.IsSuccess).IsTrue();

		// Act
		var result = await store.FirstOrDefaultAsync(
			m => m.IncrementInt32 == matchingIncrement,
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Id()).IsEqualTo(aggregateId);
	}

	[Test]
	public async Task FirstOrDefaultAsync_GivenNoMatchingAggregates_ReturnsNull(CancellationToken cancellationToken)
	{
		const int matchingIncrement = 10;

		// Arrange
		var store = Store;

		for (var i = 0; i < 10; i++)
		{
			var aggregate = CreateAggregate();
			for (var x = 0; x < matchingIncrement; x++)
				aggregate.IncrementInt32Value();

			var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		var result = await store.FirstOrDefaultAsync(m => m.IncrementInt32 == -1, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result).IsNull();
	}

	[Test]
	[MethodDataSource(typeof(SnapshotStoreContractTestData), nameof(SnapshotStoreContractTestData.CountData))]
	public async Task CountAsync_GivenAggregatesExist_ReturnsCorrectCount(
		int numberOfAggregates,
		CancellationToken cancellationToken
	)
	{
		const int numberOfEvents = 5;

		// Arrange
		var store = Store;

		for (var aggregateIndex = 0; aggregateIndex < numberOfAggregates; aggregateIndex++)
		{
			var aggregate = CreateAggregate($"agg_{aggregateIndex}");

			for (var eventIndex = 0; eventIndex < numberOfEvents; eventIndex++)
				aggregate.IncrementInt32Value();

			var saveResult = await store.SaveAsync(
				aggregate,
				CreateOperationContext(),
				cancellationToken: cancellationToken
			);
			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		var count = await store.CountAsync(
			m => m.IncrementInt32 == numberOfEvents,
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(count).IsEqualTo(numberOfAggregates);
	}

	[Test]
	public async Task CountAsync_GivenNoMatchingAggregates_ReturnsZero(CancellationToken cancellationToken)
	{
		// Arrange
		var store = Store;

		var aggregate = CreateAggregate();
		aggregate.IncrementInt32Value();

		var saveResult = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await Assert.That(saveResult.IsSuccess).IsTrue();

		// Act
		var count = await store.CountAsync(m => m.IncrementInt32 == -1, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(count).IsEqualTo(0);
	}

	[Test]
	[MethodDataSource(typeof(SnapshotStoreContractTestData), nameof(SnapshotStoreContractTestData.EnumerableCountData))]
	public async Task GetQueryEnumerableAsync_GivenAggregatesExist_EnumeratesAsExpected(
		int numberOfAggregates,
		CancellationToken cancellationToken
	)
	{
		const int numberOfEvents = 5;

		// Arrange
		var store = Store;

		for (var aggregateIndex = 0; aggregateIndex < numberOfAggregates; aggregateIndex++)
		{
			var aggregate = CreateAggregate($"agg_{aggregateIndex}");

			for (var eventIndex = 0; eventIndex < numberOfEvents; eventIndex++)
				aggregate.IncrementInt32Value();

			var saveResult = await store.SaveAsync(
				aggregate,
				CreateOperationContext(),
				cancellationToken: cancellationToken
			);
			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		List<TAggregate> aggregates = [];
		await foreach (
			var aggregate in store.GetQueryEnumerableAsync(
				m => m.IncrementInt32 == numberOfEvents,
				cancellationToken: cancellationToken
			)
		)
			aggregates.Add(aggregate);

		// Assert
		await Assert.That(aggregates.Count).IsEqualTo(numberOfAggregates);
	}

	[Test]
	[MethodDataSource(typeof(SnapshotStoreContractTestData), nameof(SnapshotStoreContractTestData.EnumerableCountData))]
	public async Task GetListEnumerableAsync_GivenAggregatesExist_EnumeratesAsExpected(
		int numberOfAggregates,
		CancellationToken cancellationToken
	)
	{
		const int numberOfEvents = 5;

		// Arrange
		var store = Store;

		for (var aggregateIndex = 0; aggregateIndex < numberOfAggregates; aggregateIndex++)
		{
			var aggregate = CreateAggregate($"agg_{aggregateIndex}");

			for (var eventIndex = 0; eventIndex < numberOfEvents; eventIndex++)
				aggregate.IncrementInt32Value();

			var saveResult = await store.SaveAsync(
				aggregate,
				CreateOperationContext(),
				cancellationToken: cancellationToken
			);
			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		List<TAggregate> aggregates = [];
		await foreach (var aggregate in store.GetListEnumerableAsync(cancellationToken: cancellationToken))
			aggregates.Add(aggregate);

		// Assert
		await Assert.That(aggregates.Count).IsEqualTo(numberOfAggregates);
	}
}
