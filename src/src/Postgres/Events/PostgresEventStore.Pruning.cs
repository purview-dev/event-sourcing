using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Postgres.Events;

partial class PostgresEventStore<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public Task<int> PruneEventsAsync(string aggregateId, long throughVersion, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
		ArgumentOutOfRangeException.ThrowIfLessThan(throughVersion, 1);

		return _client.DeleteEventsThroughVersionAsync(
			EventType,
			aggregateId,
			_aggregateTypeShortName,
			throughVersion,
			cancellationToken
		);
	}
}
