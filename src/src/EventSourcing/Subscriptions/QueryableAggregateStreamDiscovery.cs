using System.Runtime.CompilerServices;
using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Discovers streams by enumerating the queryable snapshot store.
/// </summary>
/// <remarks>
/// The snapshot store is already tenant-filtered, so the enumeration inherits that filtering. Only
/// aggregates that have been snapshotted are found: a provider configured with a snapshot strategy
/// that has not yet written one will not surface that aggregate to a subscription until it does.
/// </remarks>
/// <typeparam name="T">The aggregate type to discover streams for.</typeparam>
/// <param name="eventStore">The queryable store to enumerate.</param>
public sealed class QueryableAggregateStreamDiscovery<T>(IQueryableEventStoreCore<T> eventStore)
	: IAggregateStreamDiscovery<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public async IAsyncEnumerable<string> GetAggregateIdsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		await foreach (var aggregate in eventStore.GetListEnumerableAsync(null, cancellationToken: cancellationToken))
			yield return aggregate.Id();
	}
}
