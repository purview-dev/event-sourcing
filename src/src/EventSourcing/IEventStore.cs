using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing;

/// <summary>
/// Stores <see cref="IAggregate"/> data as a series of events, allowing for pull
/// or partial reconstitution of the state at any given point in its history.
/// </summary>
/// <seealso cref="IQueryableEventStore"/>
/// <seealso cref="IAggregate"/>
public interface IEventStore
{
	/// <summary>
	/// <para>Creates a new <see cref="IAggregate"/> of <typeparamref name="T"/> with the given <paramref name="aggregateId"/> as its Id.</para>
	/// <para>
	/// If <paramref name="aggregateId"/> is not valid, then <see cref="Services.IAggregateIdFactory.CreateAsync{T}(CancellationToken)"/>
	/// is called to generate a new id based on the <typeparamref name="T"/> provided.
	/// </para>
	/// </summary>
	/// <param name="aggregateId">Optional, the Id of the <typeparamref name="T">aggregate.</typeparamref>.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>A new <see cref="IAggregate"/> of <typeparamref name="T"/>.</returns>
	Task<T> CreateAsync<T>(string? aggregateId = null, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new();

	/// <summary>
	/// <para>Attempts to get the <see cref="IAggregate"/> of <typeparamref name="T"/> based on the <paramref name="aggregateId"/>.</para>
	/// <para>
	/// If the id is not valid (null, empty or whitespace for example), or the aggregate does not exist,
	/// a call to <see cref="CreateAsync{T}(string?, CancellationToken)"/> is
	/// made to attempt to create a new aggregate with the either the specified id or a generated one.
	/// </para>
	/// </summary>
	/// <param name="aggregateId">Optional, the id of the aggregate to get, or used as the id of the aggregate to create.</param>
	/// <param name="operationContext">The operational context, controlling things such as if and when exceptions are thrown.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>A existing or new aggregate, or null.</returns>
	/// <remarks>Null can be returned if the aggregate exists, but is in a deleted state. This is controlled by the <paramref name="operationContext"/>.</remarks>
	Task<T?> GetOrCreateAsync<T>(
		string? aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new();

	/// <summary>
	/// Gets a <see cref="IAggregate"/> of <typeparamref name="T"/> based on the given <paramref name="aggregateId"/>.
	/// </summary>
	/// <param name="aggregateId">The id of the <see cref="IAggregate"/>.</param>
	/// <param name="operationContext">The <see cref="EventStoreOperationContext"/> that controls how the aggregate is retrieved.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>The requested <see cref="IAggregate"/> of <typeparamref name="T"/> based on the <paramref name="aggregateId"/>, or null.</returns>
	Task<T?> GetAsync<T>(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new();

	/// <summary>
	/// Gets a <see cref="IAggregate"/> of <typeparamref name="T"/> based on the given <paramref name="aggregateId"/>, up to a specific version.
	/// </summary>
	/// <param name="aggregateId">The id of the <see cref="IAggregate"/>.</param>
	/// <param name="version">The version of the aggregate to get.</param>
	/// <param name="operationContext">The <see cref="EventStoreOperationContext"/> that controls how the aggregate is retrieved.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>The requested <see cref="IAggregate"/> of <typeparamref name="T"/> based on the <paramref name="aggregateId"/>, or null.</returns>
	/// <remarks><para>The resulting aggregate, if any, will be in a locked state preventing any modifications.</para>
	/// <para>This includes applying new events, or attempting to save, delete or restore the aggregate.</para>
	/// </remarks>
	Task<T?> GetAtAsync<T>(
		string aggregateId,
		int version,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new();

	/// <summary>
	/// Saves the given <paramref name="aggregate"/>.
	/// </summary>
	/// <param name="aggregate">The <see cref="IAggregate"/> to save.</param>
	/// <param name="operationContext">The <see cref="EventStoreOperationContext"/> that controls how the aggregate is saved.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An <see cref="SaveResult{TAggregate}"/> detailing the result of the save operation.</returns>
	/// <remarks>The underlying persistence store should ensure that no existing events are overridden, and
	/// the events and additional data are persisted in a transactional way.</remarks>
	Task<SaveResult<T>> SaveAsync<T>(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new();

	/// <summary>
	/// Determines if the <see cref="IAggregate"/> of type <typeparamref name="T"/> as specified by <paramref name="aggregateId"/> exists
	/// in the deleted state.
	/// </summary>
	/// <param name="aggregateId">The Id of the <see cref="IAggregate"/> to check.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>Returns true if the aggregate exists in the deleted state, otherwise, returns false.</returns>
	Task<bool> IsDeletedAsync<T>(string aggregateId, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new();

	/// <summary>
	/// Gets an <see cref="IAggregate"/> of <typeparamref name="T"/> given the specified <paramref name="aggregateId"/>.
	/// </summary>
	/// <param name="aggregateId">The Id of the deleted aggregate to get.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>If the aggregate is not found, returns null. If the aggregate was found, but is not deleted
	/// an exception is thrown. Otherwise, returns the aggregate.</returns>
	Task<T?> GetDeletedAsync<T>(string aggregateId, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new();

	/// <summary>
	/// Deletes an <see cref="IAggregate"/> from the store.
	/// </summary>
	/// <param name="aggregate">The aggregate to delete.</param>
	/// <param name="operationContext">The <see cref="EventStoreOperationContext"/> that controls how the aggregate is deleted.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>Indicates if the operation successfully deleted the aggregate.</returns>
	/// <remarks>If the <see cref="EventStoreOperationContext.PermanentlyDelete"/> is true, the aggregate and all
	/// of it's associated data is permanently deleted. If false (the default), the aggregate is soft-deleted.</remarks>
	Task<bool> DeleteAsync<T>(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new();

	/// <summary>
	/// Restores a previously deleted <see cref="IAggregate"/>.
	/// </summary>
	/// <param name="aggregate">The aggregate to restore, usually obtained from <see cref="GetDeletedAsync{T}(string, CancellationToken)"/>.</param>
	/// <param name="operationContext">The <see cref="EventStoreOperationContext"/> that controls how the aggregate is restored.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>Returns true if the aggregate was successfully restored, otherwise, false.</returns>
	Task<bool> RestoreAsync<T>(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new();

	/// <summary>
	/// Gets an <see cref="IAsyncEnumerable{T}"/> containing all of the aggregate Id, depending
	/// on the <paramref name="includeDeleted"/> parameter.
	/// </summary>
	/// <param name="includeDeleted">Indicates if the results should contain soft-deleted aggregates or not.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>The aggregate Id available.</returns>
	IAsyncEnumerable<string> GetAggregateIdsAsync<T>(bool includeDeleted, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new();

	/// <summary>
	/// Determines if the aggregate specified exists. This includes checking deleted states.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate to check.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>A <see cref="ExistsState"/> that determines the existence (in either a deleted or non-deleted state),
	/// or if it exists at all. If the aggregate does exist, it's version is also returned.</returns>
	Task<ExistsState> ExistsAsync<T>(string aggregateId, CancellationToken cancellationToken = default)
		where T : class, IAggregate, new();

	/// <summary>
	/// Fulfils any requirements of the <paramref name="aggregate"/> implemented by all <see cref="IRequirement{T}"/>s.
	/// </summary>
	/// <param name="aggregate">The aggregate to apply the requirements too.</param>
	/// <returns>The aggregate with the requirements fulfilled.</returns>
	/// <remarks>This is called automatically for any options from within the store.</remarks>
	T FulfilRequirements<T>(T aggregate)
		where T : class, IAggregate, new();

	/// <summary>
	/// Gets a range of <see cref="IEvent"/>s for a given aggregate, as specified by it's <paramref name="aggregateId"/>.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="versionFrom">The inclusive event number to start the range at.</param>
	/// <param name="versionTo">Optional, the inclusive event number to finish the range at.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>If no <paramref name="versionTo"/> is specified all available events greater than <paramref name="versionFrom"/> are returned.</returns>
	IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync<T>(
		string aggregateId,
		int versionFrom,
		int? versionTo,
		CancellationToken cancellationToken
	)
		where T : class, IAggregate, new();
}
