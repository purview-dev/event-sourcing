using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.CosmosDb.Snapshot;

partial class CosmosDbSnapshotEventStore<T>
{
	/// <inheritdoc/>
	public Task<T> CreateAsync(string? aggregateId = null, CancellationToken cancellationToken = default) =>
		_eventStore.CreateAsync(aggregateId, cancellationToken);

	/// <inheritdoc/>
	public Task<Result<T, AggregateReadFailure>> GetOrCreateAsync(
		string? aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.GetOrCreateAsync(aggregateId, operationContext, cancellationToken);

	/// <inheritdoc/>
	public Task<Result<T, AggregateReadFailure>> GetAsync(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.GetAsync(aggregateId, operationContext, cancellationToken);

	/// <inheritdoc/>
	public Task<Result<T, AggregateReadFailure>> GetAtAsync(
		string aggregateId,
		long version,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.GetAtAsync(aggregateId, version, operationContext, cancellationToken);

	/// <inheritdoc/>
	public async Task<Result<SaveOutcome<T>, AggregateSaveFailure>> SaveAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));
		var eventsApplied = aggregate.GetUnsavedEvents().Count;

		var result = await _eventStore.SaveAsync(aggregate, operationContext, cancellationToken);
		if (
			result.IsSuccess
			&& result.Value.Written
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
	/// <inheritdoc/>
	public Task<Result<T, AggregateReadFailure>> GetDeletedAsync(
		string aggregateId,
		CancellationToken cancellationToken = default
	) => _eventStore.GetDeletedAsync(aggregateId, cancellationToken);

	/// <inheritdoc/>
	public async Task<Result<SaveOutcome<T>, AggregateDeleteFailure>> DeleteAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		var result = await _eventStore.DeleteAsync(aggregate, operationContext, cancellationToken);
		if (result.IsSuccess)
			await _cosmosDbClient.DeleteAsync(aggregate.Details.Id, cancellationToken);

		return result;
	}

	/// <inheritdoc/>
	public async Task<Result<SaveOutcome<T>, AggregateRestoreFailure>> RestoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		var result = await _eventStore.RestoreAsync(aggregate, operationContext, cancellationToken);
		if (result.IsSuccess)
			await SnapshotAsync(aggregate, cancellationToken);

		return result;
	}

	/// <inheritdoc/>
	public IAsyncEnumerable<string> GetAggregateIdsAsync(
		bool includeDeleted,
		CancellationToken cancellationToken = default
	) => _eventStore.GetAggregateIdsAsync(includeDeleted, cancellationToken);

	/// <inheritdoc/>
	public Task<AggregateExistence> ExistsAsync(string aggregateId, CancellationToken cancellationToken = default) =>
		_eventStore.ExistsAsync(aggregateId, cancellationToken);

	/// <inheritdoc/>
	public T FulfilRequirements(T aggregate) => _eventStore.FulfilRequirements(aggregate);

	/// <inheritdoc/>
	public IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync(
		string aggregateId,
		long versionFrom,
		long? versionTo,
		CancellationToken cancellationToken
	) => _eventStore.GetEventRangeAsync(aggregateId, versionFrom, versionTo, cancellationToken);
}
