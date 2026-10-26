using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Internal;

namespace Purview.EventSourcing.Postgres.Events;

/// <summary>
/// The PostgreSQL event-store contract for <typeparamref name="T"/> aggregates.
/// </summary>
/// <typeparam name="T">An <see cref="IAggregate"/> implementation.</typeparam>
public interface IPostgresEventStore<T>
	: INonQueryableEventStore<T>,
		IAggregateEventHistoryStoreCore<T>,
		IIdempotencyMarkerMaintenance,
		Retention.IEventStreamPruning<T>
	where T : class, IAggregate, new()
{
	//
}
