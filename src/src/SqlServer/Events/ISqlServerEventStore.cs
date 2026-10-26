using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Internal;

namespace Purview.EventSourcing.SqlServer.Events;

/// <summary>
/// Provider-facing contract for the SQL Server event store.
/// </summary>
/// <typeparam name="T">An <see cref="IAggregate"/> implementation.</typeparam>
/// <remarks>
/// Combines the non-queryable event-store contract with aggregate event-history enumeration for SQL Server persistence.
/// </remarks>
/// <seealso cref="INonQueryableEventStore{T}"/>
/// <seealso cref="IAggregateEventHistoryStoreCore{T}"/>
public interface ISqlServerEventStore<T>
	: INonQueryableEventStore<T>,
		IAggregateEventHistoryStoreCore<T>,
		IIdempotencyMarkerMaintenance,
		Retention.IEventStreamPruning<T>
	where T : class, IAggregate, new()
{
	//
}
