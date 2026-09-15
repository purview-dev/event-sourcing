using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Test;
using Purview.EventSourcing.Internal;

namespace Purview.EventSourcing.SqlServer.Events;

public sealed class SqlServerEventStoreTransactionTests
{
	[Test]
	public async Task Enlist_GivenMismatchedTransactionalBoundaries_ThrowsInvalidOperationException()
	{
		var agg1 = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		agg1.Increment();
		var agg2 = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		agg2.Increment();

		await using SqlServerEventStoreTransaction transaction = new("sql");
		transaction.Enlist(agg1, new FakeTransactionalStore("sqlserver-primary"));

		var exception = await Assert
			.That(() => transaction.Enlist(agg2, new FakeTransactionalStore("sqlserver-secondary")))
			.Throws<InvalidOperationException>();

		await Assert.That(exception!.Message).Contains("same transaction boundary");
	}

	[Test]
	public async Task Enlist_GivenMatchingTransactionalBoundaries_AllowsMultipleStores()
	{
		var agg1 = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		agg1.Increment();
		var agg2 = TestHelpers.Aggregate<TestAggregate>(clearEvents: false);
		agg2.Increment();

		await using SqlServerEventStoreTransaction transaction = new("sql");
		transaction.Enlist(agg1, new FakeTransactionalStore("sqlserver-primary"));
		transaction.Enlist(agg2, new FakeTransactionalStore("sqlserver-primary"));

		await Assert.That(transaction.CorrelationId).IsEqualTo("sql");
	}

	sealed class FakeTransactionalStore(string boundaryKey) : ITransactionalEventStore<TestAggregate>
	{
		public string TransactionBoundaryKey => boundaryKey;

		public Task<TestAggregate> CreateAsync(
			string? aggregateId = null,
			CancellationToken cancellationToken = default
		) => throw new NotSupportedException();

		public Task<TestAggregate?> GetOrCreateAsync(
			string? aggregateId,
			EventStoreOperationContext? operationContext,
			CancellationToken cancellationToken = default
		) => throw new NotSupportedException();

		public Task<TestAggregate?> GetAsync(
			string aggregateId,
			EventStoreOperationContext? operationContext,
			CancellationToken cancellationToken = default
		) => throw new NotSupportedException();

		public Task<TestAggregate?> GetAtAsync(
			string aggregateId,
			int version,
			EventStoreOperationContext? operationContext,
			CancellationToken cancellationToken = default
		) => throw new NotSupportedException();

		public Task<SaveResult<TestAggregate>> SaveAsync(
			TestAggregate aggregate,
			EventStoreOperationContext? operationContext,
			CancellationToken cancellationToken = default
		) => throw new NotSupportedException();

		public Task<bool> IsDeletedAsync(string aggregateId, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<TestAggregate?> GetDeletedAsync(
			string aggregateId,
			CancellationToken cancellationToken = default
		) => throw new NotSupportedException();

		public Task<bool> DeleteAsync(
			TestAggregate aggregate,
			EventStoreOperationContext? operationContext,
			CancellationToken cancellationToken = default
		) => throw new NotSupportedException();

		public Task<bool> RestoreAsync(
			TestAggregate aggregate,
			EventStoreOperationContext? operationContext,
			CancellationToken cancellationToken = default
		) => throw new NotSupportedException();

		public async IAsyncEnumerable<string> GetAggregateIdsAsync(
			bool includeDeleted,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
		)
		{
			await Task.CompletedTask;
			yield break;
		}

		public Task<ExistsState> ExistsAsync(string aggregateId, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public TestAggregate FulfilRequirements(TestAggregate aggregate) => aggregate;

		public IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync(
			string aggregateId,
			int versionFrom,
			int? versionTo,
			CancellationToken cancellationToken = default
		) => throw new NotSupportedException();

		public System.Data.Common.DbConnection CreateTransactionConnection() => throw new NotSupportedException();

		public Task EnsureTransactionConfiguredAsync(
			System.Data.Common.DbConnection connection,
			CancellationToken cancellationToken = default
		) => throw new NotSupportedException();

		public Task<TransactionalSaveOperation<TestAggregate>> SaveInTransactionAsync(
			TestAggregate aggregate,
			EventStoreOperationContext? operationContext,
			System.Data.Common.DbConnection connection,
			System.Data.Common.DbTransaction transaction,
			CancellationToken cancellationToken = default
		) => throw new NotSupportedException();
	}
}
