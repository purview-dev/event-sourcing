using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Snapshotting;

namespace Purview.EventSourcing.CosmosDb.Snapshot;

partial class CosmosDbSnapshotEventStore<T>
{
	/// <inheritdoc/>
	public Task<T> CreateAsync(string? aggregateId = null, CancellationToken cancellationToken = default) =>
		_eventStore.CreateAsync(aggregateId, cancellationToken);

	/// <inheritdoc/>
	public Task<T?> GetOrCreateAsync(
		string? aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.GetOrCreateAsync(aggregateId, operationContext, cancellationToken);

	/// <inheritdoc/>
	public Task<T?> GetAsync(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.GetAsync(aggregateId, operationContext, cancellationToken);

	/// <inheritdoc/>
	public Task<T?> GetAtAsync(
		string aggregateId,
		int version,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.GetAtAsync(aggregateId, version, operationContext, cancellationToken);

	/// <inheritdoc/>
	public async Task<SaveResult<T>> SaveAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));
		var eventsApplied = aggregate.GetUnsavedEvents().Count;

		var result = await _eventStore.SaveAsync(aggregate, operationContext, cancellationToken);
		if (
			result
			&& !result.Skipped
			&& SnapshotStrategyResolver.ShouldSnapshot(
				aggregate,
				eventsApplied,
				operationContext,
				_snapshotStrategy,
				_snapshotStrategySelector
			)
		)
			await SnapshotAsync(aggregate, cancellationToken);

		return result;
	}

	/// <inheritdoc/>
	public Task<bool> IsDeletedAsync(string aggregateId, CancellationToken cancellationToken = default) =>
		_eventStore.IsDeletedAsync(aggregateId, cancellationToken);

	/// <inheritdoc/>
	public Task<T?> GetDeletedAsync(string aggregateId, CancellationToken cancellationToken = default) =>
		_eventStore.GetDeletedAsync(aggregateId, cancellationToken);

	/// <inheritdoc/>
	public async Task<bool> DeleteAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		var result = await _eventStore.DeleteAsync(aggregate, operationContext, cancellationToken);
		if (result)
			await _cosmosDbClient.DeleteAsync(aggregate.Details.Id, _partitionKey, cancellationToken);

		return result;
	}

	/// <inheritdoc/>
	public async Task<bool> RestoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		var result = await _eventStore.RestoreAsync(aggregate, operationContext, cancellationToken);
		if (result)
			await SnapshotAsync(aggregate, cancellationToken);

		return result;
	}

	/// <inheritdoc/>
	public IAsyncEnumerable<string> GetAggregateIdsAsync(
		bool includeDeleted,
		CancellationToken cancellationToken = default
	) => _eventStore.GetAggregateIdsAsync(includeDeleted, cancellationToken);

	/// <inheritdoc/>
	public Task<ExistsState> ExistsAsync(string aggregateId, CancellationToken cancellationToken = default) =>
		_eventStore.ExistsAsync(aggregateId, cancellationToken);

	/// <inheritdoc/>
	public T FulfilRequirements(T aggregate) => _eventStore.FulfilRequirements(aggregate);

	/// <inheritdoc/>
	public IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync(
		string aggregateId,
		int versionFrom,
		int? versionTo,
		CancellationToken cancellationToken
	) => _eventStore.GetEventRangeAsync(aggregateId, versionFrom, versionTo, cancellationToken);
}
