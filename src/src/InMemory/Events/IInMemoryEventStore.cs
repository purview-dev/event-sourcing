using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Internal;

namespace Purview.EventSourcing.InMemory.Events;

/// <summary>
/// An in-memory <see cref="IEventStoreCore{T}"/> implementation that persists aggregates and their
/// events in process, without queryable snapshot reads.
/// </summary>
/// <typeparam name="T">An <see cref="IAggregate"/> implementation.</typeparam>
/// <remarks>
/// Implemented by <see cref="InMemoryEventStore{T}"/>. This store is intended for testing and
/// single-process scenarios; data is not shared between instances or persisted across restarts.
/// </remarks>
/// <seealso cref="IInMemorySnapshotStore{T}"/>
public interface IInMemoryEventStore<T> : INonQueryableEventStore<T>, IAggregateEventHistoryStoreCore<T>
	where T : class, IAggregate, new()
{
	/// <summary>
	/// Removes all aggregates and events from the store.
	/// </summary>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>A task that completes when the store has been cleared.</returns>
	Task ClearAsync(CancellationToken cancellationToken = default);
}
