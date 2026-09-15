using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Internal;

namespace Purview.EventSourcing;

/// <summary>
/// Default <see cref="IQueryableEventStore"/> implementation that resolves the typed
/// <see cref="IQueryableEventStoreCore{T}"/> for each aggregate type from the service provider.
/// </summary>
/// <remarks>
/// <para>
/// The facade caches one <see cref="IQueryableEventStoreCore{T}"/> per aggregate type and forwards every
/// operation to it, providing a single non-generic entry point for both event and snapshot queries. It is
/// hidden from IntelliSense because applications typically consume it through the <see cref="IQueryableEventStore"/>
/// interface.
/// </para>
/// <para>
/// It also implements <see cref="IQueryableEventStoreImplementationAccessor"/> so history and transaction
/// extensions can reach the underlying typed store.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class QueryableEventStoreFacade(IServiceProvider serviceProvider)
	: IQueryableEventStore,
		IQueryableEventStoreImplementationAccessor
{
	readonly ConcurrentDictionary<Type, object> _queryableEventStores = new();

	///<inheritdoc/>
	public Task<T> CreateAsync<T>(string? aggregateId = null, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new() => GetQueryableEventStore<T>().CreateAsync(aggregateId, cancellationToken);

	///<inheritdoc/>
	public Task<T?> GetOrCreateAsync<T>(
		string? aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().GetOrCreateAsync(aggregateId, operationContext, cancellationToken);

	///<inheritdoc/>
	public Task<T?> GetAsync<T>(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().GetAsync(aggregateId, operationContext, cancellationToken);

	///<inheritdoc/>
	public Task<T?> GetAtAsync<T>(
		string aggregateId,
		int version,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().GetAtAsync(aggregateId, version, operationContext, cancellationToken);

	///<inheritdoc/>
	public Task<SaveResult<T>> SaveAsync<T>(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().SaveAsync(aggregate, operationContext, cancellationToken);

	///<inheritdoc/>
	public Task<bool> IsDeletedAsync<T>(string aggregateId, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().IsDeletedAsync(aggregateId, cancellationToken);

	///<inheritdoc/>
	public Task<T?> GetDeletedAsync<T>(string aggregateId, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().GetDeletedAsync(aggregateId, cancellationToken);

	///<inheritdoc/>
	public Task<bool> DeleteAsync<T>(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().DeleteAsync(aggregate, operationContext, cancellationToken);

	///<inheritdoc/>
	public Task<bool> RestoreAsync<T>(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().RestoreAsync(aggregate, operationContext, cancellationToken);

	///<inheritdoc/>
	public IAsyncEnumerable<string> GetAggregateIdsAsync<T>(
		bool includeDeleted,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().GetAggregateIdsAsync(includeDeleted, cancellationToken);

	///<inheritdoc/>
	public Task<ExistsState> ExistsAsync<T>(string aggregateId, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new() => GetQueryableEventStore<T>().ExistsAsync(aggregateId, cancellationToken);

	///<inheritdoc/>
	public T FulfilRequirements<T>(T aggregate)
		where T : class, IAggregate, new() => GetQueryableEventStore<T>().FulfilRequirements(aggregate);

	///<inheritdoc/>
	public IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync<T>(
		string aggregateId,
		int versionFrom,
		int? versionTo,
		CancellationToken cancellationToken
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().GetEventRangeAsync(aggregateId, versionFrom, versionTo, cancellationToken);

	///<inheritdoc/>
	public IAsyncEnumerable<T> GetQueryEnumerableAsync<T>(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		int maxRecordsPerIteration = ContinuationRequest.DefaultMaxRecords,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>()
			.GetQueryEnumerableAsync(whereClause, orderByClause, maxRecordsPerIteration, cancellationToken);

	///<inheritdoc/>
	public IAsyncEnumerable<T> GetListEnumerableAsync<T>(
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		int maxRecordsPerIteration = ContinuationRequest.DefaultMaxRecords,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().GetListEnumerableAsync(orderByClause, maxRecordsPerIteration, cancellationToken);

	///<inheritdoc/>
	public Task<ContinuationResponse<T>> QueryAsync<T>(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		ContinuationRequest request,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().QueryAsync(whereClause, orderByClause, request, cancellationToken);

	///<inheritdoc/>
	public Task<ContinuationResponse<T>> ListAsync<T>(
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		ContinuationRequest request,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().ListAsync(orderByClause, request, cancellationToken);

	///<inheritdoc/>
	public Task<long> CountAsync<T>(
		Expression<Func<T, bool>>? whereClause,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() => GetQueryableEventStore<T>().CountAsync(whereClause, cancellationToken);

	///<inheritdoc/>
	public Task<T?> SingleOrDefaultAsync<T>(
		Expression<Func<T, bool>> whereClause,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().SingleOrDefaultAsync(whereClause, cancellationToken);

	///<inheritdoc/>
	public Task<T?> FirstOrDefaultAsync<T>(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetQueryableEventStore<T>().FirstOrDefaultAsync(whereClause, orderByClause, cancellationToken);

	///<inheritdoc/>
	public IEventStoreCore<T> GetEventStore<T>()
		where T : class, IAggregate, new() => GetQueryableEventStore<T>();

	///<inheritdoc/>
	public IQueryableEventStoreCore<T> GetQueryableEventStore<T>()
		where T : class, IAggregate, new() =>
		(IQueryableEventStoreCore<T>)
			_queryableEventStores.GetOrAdd(
				typeof(T),
				_ => serviceProvider.GetRequiredService<IQueryableEventStoreCore<T>>()
			);
}
