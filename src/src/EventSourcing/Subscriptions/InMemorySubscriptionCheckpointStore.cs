using System.Collections.Concurrent;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// A non-persistent <see cref="ISubscriptionCheckpointStore"/> held in process memory.
/// </summary>
/// <remarks>
/// Positions are lost on restart, so every subscription replays its streams from the beginning.
/// That is correct but rarely desirable: use this for tests and local development, and register a
/// durable store in a deployed application.
/// </remarks>
public sealed class InMemorySubscriptionCheckpointStore : ISubscriptionCheckpointStore
{
	readonly ConcurrentDictionary<SubscriptionCheckpointKey, long> _checkpoints = new();

	/// <inheritdoc/>
	public Task<long?> GetAsync(SubscriptionCheckpointKey key, CancellationToken cancellationToken) =>
		Task.FromResult(_checkpoints.TryGetValue(key, out var version) ? version : (long?)null);

	/// <inheritdoc/>
	public Task SetAsync(SubscriptionCheckpointKey key, long version, CancellationToken cancellationToken)
	{
		// A cycle that re-read an earlier range must not move the position backwards.
		_checkpoints.AddOrUpdate(key, version, (_, existing) => Math.Max(existing, version));

		return Task.CompletedTask;
	}
}
