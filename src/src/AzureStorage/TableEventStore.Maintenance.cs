using Azure.Data.Tables;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.AzureStorage.Entities;

namespace Purview.EventSourcing.AzureStorage;

partial class TableEventStore<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public async Task<int> CleanupIdempotencyMarkersAsync(TimeSpan retention, CancellationToken cancellationToken)
	{
		// A zero or negative retention would delete markers for saves still in flight, which
		// reintroduces exactly the duplicate they exist to prevent.
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);

		var cutoff = DateTimeOffset.UtcNow - retention;

		// Markers are distinguished by a row-key prefix rather than a column, so the range filter is
		// how they are selected. The successor character bounds the prefix without matching anything
		// beyond it.
		var prefix = TableEventStoreConstants.IdempotencyCheckRowKeyPrefix;
		var upperBound = prefix[..^1] + (char)(prefix[^1] + 1);
		var filter =
			$"({nameof(ITableEntity.RowKey)} ge '{prefix}') and ({nameof(ITableEntity.RowKey)} lt '{upperBound}')";

		// The table client applies the tenant filter, so a tenant clears only its own markers and
		// the system scope clears every tenant's.
		var expired = _tableClient.QueryEnumerableAsync<IdempotencyMarkerEntity>(
			filter,
			cancellationToken: cancellationToken
		);

		var removed = 0;
		await foreach (var marker in expired)
		{
			// Timestamp is assigned by the service, so it is filtered here rather than in the query:
			// combining it with the row-key range makes the scan no cheaper, and a marker written in
			// the last moment must not be removed.
			if (marker.Timestamp is null || marker.Timestamp >= cutoff)
				continue;

			await _tableClient.OperationAsync(TableTransactionActionType.Delete, marker, cancellationToken);
			removed++;
		}

		return removed;
	}
}
