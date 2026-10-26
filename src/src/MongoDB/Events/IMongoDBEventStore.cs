using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Internal;

namespace Purview.EventSourcing.MongoDB.Events;

/// <summary>
/// The MongoDB-backed, non-queryable event store contract for <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">An <see cref="IAggregate"/> implementation.</typeparam>
/// <remarks>
/// Combines the <see cref="INonQueryableEventStore{T}"/> and <see cref="IAggregateEventHistoryStoreCore{T}"/>
/// contracts. Persists events to MongoDB collections and supports enumerating events by version range,
/// but does not maintain a queryable snapshot model.
/// </remarks>
/// <seealso cref="MongoDBEventStore{T}"/>
public interface IMongoDBEventStore<T>
	: INonQueryableEventStore<T>,
		IAggregateEventHistoryStoreCore<T>,
		IIdempotencyMarkerMaintenance
	where T : class, IAggregate, new()
{
	//
}
