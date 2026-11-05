using System.Collections.Concurrent;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Counts failed deliveries in memory, for the lifetime of the process.
/// </summary>
/// <remarks>
/// Entries are removed as soon as an event is delivered or dead-lettered, so the dictionary holds
/// only events currently failing - which, in a healthy deployment, is none.
/// </remarks>
public sealed class InMemorySubscriptionDeliveryAttemptTracker : ISubscriptionDeliveryAttemptTracker
{
	readonly ConcurrentDictionary<(SubscriptionCheckpointKey Key, long Version), int> _attempts = new();

	/// <inheritdoc/>
	public int RecordFailure(SubscriptionCheckpointKey key, long aggregateVersion) =>
		_attempts.AddOrUpdate((key, aggregateVersion), 1, (_, attempts) => attempts + 1);

	/// <inheritdoc/>
	public void Clear(SubscriptionCheckpointKey key, long aggregateVersion) =>
		_attempts.TryRemove((key, aggregateVersion), out _);
}
