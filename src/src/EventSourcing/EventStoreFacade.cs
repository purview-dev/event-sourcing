using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Internal;

namespace Purview.EventSourcing;

/// <summary>
/// Default <see cref="IEventStore"/> implementation that resolves the typed
/// <see cref="IEventStoreCore{T}"/> for each aggregate type from the service provider.
/// </summary>
/// <remarks>
/// <para>
/// The facade caches one <see cref="IEventStoreCore{T}"/> per aggregate type and forwards every operation to
/// it, providing a single non-generic entry point for the application. It is hidden from IntelliSense because
/// applications typically consume it through the <see cref="IEventStore"/> interface.
/// </para>
/// <para>
/// It also implements <see cref="IEventStoreImplementationAccessor"/> so history and transaction extensions can
/// reach the underlying typed store.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class EventStoreFacade(IServiceProvider serviceProvider) : IEventStore, IEventStoreImplementationAccessor
{
	readonly ConcurrentDictionary<Type, object> _eventStores = new();

	///<inheritdoc/>
	public Task<T> CreateAsync<T>(string? aggregateId = null, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new() => GetEventStore<T>().CreateAsync(aggregateId, cancellationToken);

	///<inheritdoc/>
	public Task<T?> GetOrCreateAsync<T>(
		string? aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetEventStore<T>().GetOrCreateAsync(aggregateId, operationContext, cancellationToken);

	///<inheritdoc/>
	public Task<T?> GetAsync<T>(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetEventStore<T>().GetAsync(aggregateId, operationContext, cancellationToken);

	///<inheritdoc/>
	public Task<T?> GetAtAsync<T>(
		string aggregateId,
		int version,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetEventStore<T>().GetAtAsync(aggregateId, version, operationContext, cancellationToken);

	///<inheritdoc/>
	public Task<SaveResult<T>> SaveAsync<T>(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetEventStore<T>().SaveAsync(aggregate, operationContext, cancellationToken);

	///<inheritdoc/>
	public Task<bool> IsDeletedAsync<T>(string aggregateId, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new() => GetEventStore<T>().IsDeletedAsync(aggregateId, cancellationToken);

	///<inheritdoc/>
	public Task<T?> GetDeletedAsync<T>(string aggregateId, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new() => GetEventStore<T>().GetDeletedAsync(aggregateId, cancellationToken);

	///<inheritdoc/>
	public Task<bool> DeleteAsync<T>(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetEventStore<T>().DeleteAsync(aggregate, operationContext, cancellationToken);

	///<inheritdoc/>
	public Task<bool> RestoreAsync<T>(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetEventStore<T>().RestoreAsync(aggregate, operationContext, cancellationToken);

	///<inheritdoc/>
	public IAsyncEnumerable<string> GetAggregateIdsAsync<T>(
		bool includeDeleted,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() =>
		GetEventStore<T>().GetAggregateIdsAsync(includeDeleted, cancellationToken);

	///<inheritdoc/>
	public Task<ExistsState> ExistsAsync<T>(string aggregateId, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new() => GetEventStore<T>().ExistsAsync(aggregateId, cancellationToken);

	///<inheritdoc/>
	public T FulfilRequirements<T>(T aggregate)
		where T : class, IAggregate, new() => GetEventStore<T>().FulfilRequirements(aggregate);

	///<inheritdoc/>
	public IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync<T>(
		string aggregateId,
		int versionFrom,
		int? versionTo,
		CancellationToken cancellationToken
	)
		where T : class, IAggregate, new() =>
		GetEventStore<T>().GetEventRangeAsync(aggregateId, versionFrom, versionTo, cancellationToken);

	///<inheritdoc/>
	public IEventStoreCore<T> GetEventStore<T>()
		where T : class, IAggregate, new() =>
		(IEventStoreCore<T>)
			_eventStores.GetOrAdd(typeof(T), _ => serviceProvider.GetRequiredService<IEventStoreCore<T>>());
}
