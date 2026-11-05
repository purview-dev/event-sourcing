using MongoDB.Driver;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.MongoDB.Events.Entities;

namespace Purview.EventSourcing.MongoDB;

partial class MongoDBEventStore<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public Task<int> PruneEventsAsync(string aggregateId, long throughVersion, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
		ArgumentOutOfRangeException.ThrowIfLessThan(throughVersion, 1);

		var filter = Builders<EventEntity>.Filter.And(
			Builders<EventEntity>.Filter.Eq(entity => entity.EntityType, EntityTypes.EventType),
			Builders<EventEntity>.Filter.Eq(entity => entity.AggregateId, aggregateId),
			Builders<EventEntity>.Filter.Lte(entity => entity.Version, throughVersion)
		);

		// Scoped by the client, so a stream belonging to another tenant cannot be deleted by a scope
		// that does not own it.
		return _eventClient.DeleteManyAsync(filter, cancellationToken);
	}
}
