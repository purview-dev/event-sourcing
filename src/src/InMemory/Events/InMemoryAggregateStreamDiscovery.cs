using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Subscriptions;

namespace Purview.EventSourcing.InMemory.Events;

/// <summary>
/// Discovers streams directly from the in-memory store.
/// </summary>
/// <remarks>
/// The in-memory store has no queryable snapshot reads, so the queryable default cannot be used. It
/// does hold every stream it has written, which makes discovery exact rather than dependent on a
/// snapshot having been taken.
/// </remarks>
/// <typeparam name="T">The aggregate type to discover streams for.</typeparam>
/// <param name="eventStore">The store to enumerate.</param>
public sealed class InMemoryAggregateStreamDiscovery<T>(IInMemoryEventStore<T> eventStore)
	: IAggregateStreamDiscovery<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public IAsyncEnumerable<string> GetAggregateIdsAsync(CancellationToken cancellationToken) =>
		eventStore.GetAggregateIdsAsync(cancellationToken);
}
