using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Finds the aggregate streams a subscription should read.
/// </summary>
/// <remarks>
/// <para>
/// Event streams are keyed by aggregate id, and no provider holds an index of "every stream that
/// exists". Discovery is therefore a seam rather than a fixed implementation: a provider with a
/// queryable snapshot store can enumerate it (<see cref="QueryableAggregateStreamDiscovery{T}"/>),
/// while others need their own.
/// </para>
/// <para>
/// Implementations must honour the current tenant. An implementation that enumerates every stream
/// regardless of scope hands one tenant's aggregate ids to another, even if the subsequent event
/// read is filtered.
/// </para>
/// </remarks>
/// <typeparam name="T">The aggregate type to discover streams for.</typeparam>
public interface IAggregateStreamDiscovery<T>
	where T : class, IAggregate, new()
{
	/// <summary>
	/// Enumerates the ids of the aggregates visible to the current tenant, in an unspecified order.
	/// </summary>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	IAsyncEnumerable<string> GetAggregateIdsAsync(CancellationToken cancellationToken);
}
