using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.InMemory.Events;

partial class InMemoryEventStore<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public Task<int> PruneEventsAsync(string aggregateId, long throughVersion, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
		ArgumentOutOfRangeException.ThrowIfLessThan(throughVersion, 1);

		// Filtered the same way every read is: a stream owned by another tenant is invisible here and
		// cannot be pruned by a scope that does not own it.
		if (!IsVisible(aggregateId) || !_events.TryGetValue(aggregateId, out var eventList))
			return Task.FromResult(0);

		var removed = 0;
		foreach (var version in eventList.Keys.Where(version => version <= throughVersion).ToArray())
		{
			if (eventList.TryRemove(version, out _))
				removed++;
		}

		return Task.FromResult(removed);
	}
}
