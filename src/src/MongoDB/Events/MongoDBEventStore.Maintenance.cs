using MongoDB.Driver;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.MongoDB.Events.Entities;

namespace Purview.EventSourcing.MongoDB;

partial class MongoDBEventStore<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public Task<int> CleanupIdempotencyMarkersAsync(TimeSpan retention, CancellationToken cancellationToken)
	{
		// A zero or negative retention would delete markers for saves still in flight, which
		// reintroduces exactly the duplicate they exist to prevent.
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);

		var cutoff = DateTimeOffset.UtcNow - retention;
		var filter = Builders<IdempotencyMarkerEntity>.Filter.And(
			Builders<IdempotencyMarkerEntity>.Filter.Eq(marker => marker.EntityType, EntityTypes.IdempotencyMarkerType),
			Builders<IdempotencyMarkerEntity>.Filter.Lt(marker => marker.Timestamp, cutoff)
		);

		return _eventClient.DeleteManyAsync(filter, cancellationToken);
	}
}
