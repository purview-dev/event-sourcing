namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Persists how far a subscription has read each aggregate's stream.
/// </summary>
/// <remarks>
/// A checkpoint is written only after the subscriber has handled the event, so a crash between the
/// two redelivers rather than skips. Delivery is therefore at-least-once and subscribers must be
/// idempotent.
/// </remarks>
public interface ISubscriptionCheckpointStore
{
	/// <summary>
	/// Gets the last aggregate version delivered for <paramref name="key"/>, or
	/// <see langword="null"/> when the subscription has not read this stream before.
	/// </summary>
	/// <param name="key">The subscription, tenant and aggregate the position belongs to.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	Task<long?> GetAsync(SubscriptionCheckpointKey key, CancellationToken cancellationToken);

	/// <summary>
	/// Records that every event up to and including <paramref name="version"/> has been delivered.
	/// </summary>
	/// <param name="key">The subscription, tenant and aggregate the position belongs to.</param>
	/// <param name="version">The last delivered aggregate version.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	Task SetAsync(SubscriptionCheckpointKey key, long version, CancellationToken cancellationToken);
}
