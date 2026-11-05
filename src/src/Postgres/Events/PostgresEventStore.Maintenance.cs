using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Postgres.Events;

partial class PostgresEventStore<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public Task<int> CleanupIdempotencyMarkersAsync(TimeSpan retention, CancellationToken cancellationToken)
	{
		// A zero or negative retention would delete markers for saves still in flight, which
		// reintroduces exactly the duplicate they exist to prevent.
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);

		return _client.DeleteIdempotencyMarkersOlderThanAsync(
			IdempotencyMarkerType,
			DateTimeOffset.UtcNow - retention,
			cancellationToken
		);
	}
}
