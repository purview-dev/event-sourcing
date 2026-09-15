using System.ComponentModel;
using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing;

/// <summary>
/// Provider-facing typed event-store contract used by concrete implementations and internal infrastructure.
/// </summary>
/// <typeparam name="T">An <see cref="IAggregate"/> implementation.</typeparam>
/// <seealso cref="IQueryableEventStoreCore{T}"/>
/// <seealso cref="IAggregate"/>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IEventStoreCore<T>
	where T : class, IAggregate, new()
{
	/// <summary>
	/// Creates a new aggregate of <typeparamref name="T"/> with the given <paramref name="aggregateId"/>.
	/// </summary>
	/// <param name="aggregateId">Optional, the id of the aggregate to create; when null, an id is generated.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The newly created aggregate.</returns>
	Task<T> CreateAsync(string? aggregateId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the aggregate for the id, creating it when it does not yet exist.
	/// </summary>
	/// <param name="aggregateId">Optional, the id of the aggregate to get, or use as the id of the aggregate to create.</param>
	/// <param name="operationContext">The operational context controlling how the aggregate is retrieved.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The existing or newly created aggregate, or null.</returns>
	Task<T?> GetOrCreateAsync(
		string? aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	);

	/// <summary>
	/// Gets the aggregate for the id.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate to get.</param>
	/// <param name="operationContext">The operational context controlling how the aggregate is retrieved.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The requested aggregate, or null when it does not exist.</returns>
	Task<T?> GetAsync(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	);

	/// <summary>
	/// Gets the aggregate up to a specific version.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate to get.</param>
	/// <param name="version">The version of the aggregate to get.</param>
	/// <param name="operationContext">The operational context controlling how the aggregate is retrieved.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The requested aggregate, or null when it does not exist.</returns>
	Task<T?> GetAtAsync(
		string aggregateId,
		int version,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	);

	/// <summary>
	/// Saves the aggregate and its unsaved events.
	/// </summary>
	/// <param name="aggregate">The aggregate to save.</param>
	/// <param name="operationContext">The operational context controlling how the aggregate is saved.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>A <see cref="SaveResult{T}"/> describing the result of the save.</returns>
	Task<SaveResult<T>> SaveAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	);

	/// <summary>
	/// Determines if the aggregate exists in the deleted state.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate to check.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>True when the aggregate exists in the deleted state, otherwise false.</returns>
	Task<bool> IsDeletedAsync(string aggregateId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets a deleted aggregate.
	/// </summary>
	/// <param name="aggregateId">The id of the deleted aggregate to get.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The deleted aggregate, or null when it is not found.</returns>
	Task<T?> GetDeletedAsync(string aggregateId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes the aggregate.
	/// </summary>
	/// <param name="aggregate">The aggregate to delete.</param>
	/// <param name="operationContext">The operational context controlling how the aggregate is deleted.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>True when the aggregate was successfully deleted, otherwise false.</returns>
	Task<bool> DeleteAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	);

	/// <summary>
	/// Restores a previously deleted aggregate.
	/// </summary>
	/// <param name="aggregate">The aggregate to restore.</param>
	/// <param name="operationContext">The operational context controlling how the aggregate is restored.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>True when the aggregate was successfully restored, otherwise false.</returns>
	Task<bool> RestoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	);

	/// <summary>
	/// Enumerates the ids of all aggregates, optionally including soft-deleted aggregates.
	/// </summary>
	/// <param name="includeDeleted">Whether soft-deleted aggregates should be included.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>An asynchronous enumerable of aggregate ids.</returns>
	IAsyncEnumerable<string> GetAggregateIdsAsync(bool includeDeleted, CancellationToken cancellationToken = default);

	/// <summary>
	/// Determines if the aggregate exists, including deleted states.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate to check.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>An <see cref="ExistsState"/> describing the existence of the aggregate.</returns>
	Task<ExistsState> ExistsAsync(string aggregateId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Fulfils the aggregate's requirements by injecting the required services.
	/// </summary>
	/// <param name="aggregate">The aggregate whose requirements should be fulfilled.</param>
	/// <returns>The same aggregate with its requirements fulfilled.</returns>
	T FulfilRequirements(T aggregate);

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
