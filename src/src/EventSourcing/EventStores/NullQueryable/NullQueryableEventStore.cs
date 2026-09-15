using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Internal;

namespace Purview.EventSourcing.EventStores.NullQueryable;

sealed class NullQueryableEventStore<T>(INonQueryableEventStore<T> eventStore) : IQueryableEventStoreCore<T>
	where T : class, IAggregate, new()
{
	readonly IEventStoreCore<T> _eventStore = eventStore;

	public T FulfilRequirements(T aggregate) => _eventStore.FulfilRequirements(aggregate);

	public IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync(
		string aggregateId,
		int versionFrom,
		int? versionTo,
		CancellationToken cancellationToken
	) => _eventStore.GetEventRangeAsync(aggregateId, versionFrom, versionTo, cancellationToken);

	public Task<T> CreateAsync(string? id = null, CancellationToken cancellationToken = default) =>
		_eventStore.CreateAsync(id, cancellationToken);

	public Task<bool> DeleteAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.DeleteAsync(aggregate, operationContext, cancellationToken);

	public Task<ExistsState> ExistsAsync(string id, CancellationToken cancellationToken = default) =>
		_eventStore.ExistsAsync(id, cancellationToken);

	public IAsyncEnumerable<string> GetAggregateIdsAsync(
		bool includeDeleted,
		CancellationToken cancellationToken = default
	) => _eventStore.GetAggregateIdsAsync(includeDeleted, cancellationToken);

	public Task<T?> GetAsync(
		string id,
		EventStoreOperationContext? context,
		CancellationToken cancellationToken = default
	) => _eventStore.GetAsync(id, context, cancellationToken);

	public Task<T?> GetAtAsync(
		string id,
		int version,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.GetAtAsync(id, version, operationContext, cancellationToken);

	public Task<T?> GetDeletedAsync(string id, CancellationToken cancellationToken = default) =>
		_eventStore.GetDeletedAsync(id, cancellationToken);

	public Task<T?> GetOrCreateAsync(
		string? id,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.GetOrCreateAsync(id, operationContext, cancellationToken);

	public Task<bool> IsDeletedAsync(string id, CancellationToken cancellationToken = default) =>
		_eventStore.IsDeletedAsync(id, cancellationToken);

	public Task<ContinuationResponse<T>> QueryAsync(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		ContinuationRequest request,
		CancellationToken cancellationToken = default
	) => Task.FromResult(new ContinuationResponse<T>() { Results = [], RequestedCount = request.MaxRecords });

	public Task<ContinuationResponse<T>> ListAsync(
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		ContinuationRequest request,
		CancellationToken cancellationToken = default
	) => Task.FromResult(new ContinuationResponse<T>() { Results = [], RequestedCount = request.MaxRecords });

	public Task<bool> RestoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.RestoreAsync(aggregate, operationContext, cancellationToken);

	public Task<SaveResult<T>> SaveAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.SaveAsync(aggregate, operationContext, cancellationToken);

	public Task<T?> SingleOrDefaultAsync(
		Expression<Func<T, bool>> whereClause,
		CancellationToken cancellationToken = default
	) => Task.FromResult<T?>(default);

	public Task<long> CountAsync(
		Expression<Func<T, bool>>? whereClause,
		CancellationToken cancellationToken = default
	) => Task.FromResult(0L);

	public Task<T?> FirstOrDefaultAsync(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		CancellationToken cancellationToken = default
	) => Task.FromResult<T?>(default);

	public async IAsyncEnumerable<T> GetQueryEnumerableAsync(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		int maxRecordsPerIteration = ContinuationRequest.DefaultMaxRecords,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
	{
		await Task.CompletedTask;

		yield break;
	}

	public async IAsyncEnumerable<T> GetListEnumerableAsync(
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		int maxRecordsPerIteration = ContinuationRequest.DefaultMaxRecords,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
	{
		await Task.CompletedTask;

		yield break;
	}
}
