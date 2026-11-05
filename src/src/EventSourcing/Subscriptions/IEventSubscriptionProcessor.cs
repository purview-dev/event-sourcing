using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Runs a subscription's catch-up cycles.
/// </summary>
/// <typeparam name="T">The aggregate type the subscription reads.</typeparam>
public interface IEventSubscriptionProcessor<T>
	where T : class, IAggregate, new()
{
	/// <summary>
	/// Reads every visible stream from its recorded position to the end, delivering each event.
	/// </summary>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>What the cycle did.</returns>
	Task<SubscriptionCycleResult> RunCycleAsync(CancellationToken cancellationToken);
}
