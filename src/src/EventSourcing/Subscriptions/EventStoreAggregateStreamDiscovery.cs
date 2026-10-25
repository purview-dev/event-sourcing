using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// The default <see cref="IAggregateStreamDiscovery{T}"/>, over
/// <see cref="IEventStoreCore{T}.GetAggregateIdsAsync(bool, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every store implements that method and filters it by the current tenant, so this works on every
/// provider that persists an event stream, with no snapshot store required.
/// </para>
/// <para>
/// Deleted aggregates are included. A subscription that stopped seeing a stream the moment it was
/// deleted would never deliver the <c>Deleted</c> event, so a projection could not learn that the
/// aggregate had gone.
/// </para>
/// </remarks>
/// <typeparam name="T">The aggregate type to discover streams for.</typeparam>
/// <param name="eventStore">The store to enumerate.</param>
public sealed class EventStoreAggregateStreamDiscovery<T>(IEventStoreCore<T> eventStore) : IAggregateStreamDiscovery<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public IAsyncEnumerable<string> GetAggregateIdsAsync(CancellationToken cancellationToken) =>
		eventStore.GetAggregateIdsAsync(includeDeleted: true, cancellationToken);
}
