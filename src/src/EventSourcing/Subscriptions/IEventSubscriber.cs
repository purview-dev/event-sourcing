using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Receives events from an aggregate's stream, in version order, including events written before the
/// subscriber existed.
/// </summary>
/// <remarks>
/// <para>
/// This is the difference between a subscription and the change feed or the outbox: both of those
/// only carry what happened while they were running, so a projection added today cannot use them to
/// rebuild from history. A subscription reads the stream itself.
/// </para>
/// <para>
/// Delivery is at-least-once, so implementations must be idempotent. Ordering is guaranteed within
/// one aggregate and not between aggregates.
/// </para>
/// </remarks>
/// <typeparam name="T">The aggregate type to subscribe to.</typeparam>
public interface IEventSubscriber<T>
	where T : class, IAggregate, new()
{
	/// <summary>
	/// The subscription's stable name. Positions are recorded against it, so renaming a live
	/// subscription abandons its progress and replays every stream from the beginning.
	/// </summary>
	string SubscriptionName { get; }

	/// <summary>
	/// Handles one event.
	/// </summary>
	/// <param name="context">The event, its metadata, and the tenant it belongs to.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <remarks>
	/// Throwing stops this aggregate's stream at the failing event and retries it on the next cycle.
	/// Events already delivered in this cycle stay delivered, and other aggregates are unaffected.
	/// </remarks>
	Task OnEventAsync(EventSubscriptionContext<T> context, CancellationToken cancellationToken);
}
