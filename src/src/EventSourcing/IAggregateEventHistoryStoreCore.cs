using System.ComponentModel;
using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing;

/// <summary>
/// Provider-facing contract for stores that can enumerate aggregate events by version range.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IAggregateEventHistoryStoreCore<T>
	where T : class, IAggregate, new()
{
	/// <summary>
	/// Enumerates a range of events for the aggregate.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="versionFrom">The inclusive event number to start the range at.</param>
	/// <param name="versionTo">Optional, the inclusive event number to finish the range at.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The events and their persisted names in the requested range.</returns>
	IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync(
		string aggregateId,
		int versionFrom,
		int? versionTo,
		CancellationToken cancellationToken
	);
}
