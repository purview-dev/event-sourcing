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
public sealed class InMemorySubscriptionCheckpointStore
	: ISubscriptionCheckpointStore,
		ISubscriptionCheckpointMaintenance
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

	/// <inheritdoc/>
	public Task<int> RemoveSubscriptionAsync(
		string subscriptionName,
		Tenancy.TenantScope scope,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionName);

		var removed = 0;
		foreach (var key in _checkpoints.Keys)
		{
			// Scope matched exactly, the same way a read matches it. A subscription name alone would
			// remove every tenant's position, which is not what the caller asked for.
			if (!string.Equals(key.SubscriptionName, subscriptionName, StringComparison.Ordinal) || key.Scope != scope)
				continue;

			if (_checkpoints.TryRemove(key, out _))
				removed++;
		}

		return Task.FromResult(removed);
	}

	/// <inheritdoc/>
	public Task<bool> RemoveCheckpointAsync(SubscriptionCheckpointKey key, CancellationToken cancellationToken) =>
		Task.FromResult(_checkpoints.TryRemove(key, out _));
}
