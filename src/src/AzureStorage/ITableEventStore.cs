using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Internal;

namespace Purview.EventSourcing.AzureStorage;

/// <summary>
/// Provider-facing contract for the Azure Table and Blob Storage event store.
/// </summary>
/// <typeparam name="T">The <see cref="IAggregate"/> type the store persists.</typeparam>
/// <remarks>
/// Combines the non-queryable event-store contract with the aggregate event-history contract.
/// The concrete implementation, <see cref="TableEventStore{T}"/>, persists events to Azure Table Storage
/// and snapshots and large events to Azure Blob Storage.
/// </remarks>
public interface ITableEventStore<T>
	: INonQueryableEventStore<T>,
		IAggregateEventHistoryStoreCore<T>,
		IIdempotencyMarkerMaintenance
	where T : class, IAggregate, new()
{
	//
}
