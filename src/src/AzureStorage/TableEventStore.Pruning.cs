using Azure.Data.Tables;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.AzureStorage.Entities;

namespace Purview.EventSourcing.AzureStorage;

partial class TableEventStore<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public async Task<int> PruneEventsAsync(
		string aggregateId,
		long throughVersion,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
		ArgumentOutOfRangeException.ThrowIfLessThan(throughVersion, 1);

		// Row keys encode a zero-padded version, so a lexical range is a version range. The lower
		// bound is version 1 rather than the prefix alone, which would also match the stream-version
		// and idempotency rows sharing this partition.
		var filter =
			$"({nameof(ITableEntity.PartitionKey)} eq '{aggregateId}') "
			+ $"and ({nameof(ITableEntity.RowKey)} ge '{CreateEventRowKey(1)}') "
			+ $"and ({nameof(ITableEntity.RowKey)} le '{CreateEventRowKey(throughVersion)}')";

		// The table client applies the tenant filter, so a stream owned by another tenant is not
		// visible here and cannot be deleted by a scope that does not own it.
		var doomed = _tableClient.QueryEnumerableAsync<EventEntity>(filter, cancellationToken: cancellationToken);

		var removed = 0;
		await foreach (var entity in doomed)
		{
			// An oversized event lives in a blob behind a pointer row. Deleting the row without the
			// blob leaks storage that nothing will ever reference again, and nothing would report it.
			await DeleteEventBlobIfPresentAsync(entity, cancellationToken);

			await _tableClient.OperationAsync(TableTransactionActionType.Delete, entity, cancellationToken);
			removed++;
		}

		return removed;
	}

	async Task DeleteEventBlobIfPresentAsync(EventEntity entity, CancellationToken cancellationToken)
	{
		// Only a pointer event has a blob. Checking the mapped event name avoids a blob round trip
		// for every ordinary event in the range.
		var eventTypeName = _eventNameMapper.GetTypeName<T>(entity.EventType);
		if (
			eventTypeName is null
			|| !eventTypeName.Contains(nameof(Events.LargeEventPointerEvent), StringComparison.Ordinal)
		)
			return;

		var blobName = GenerateEventBlobName(entity.PartitionKey, entity.RowKey);

		await _blobClient.DeleteBlobIfExistsAsync(blobName, cancellationToken);
	}
}
