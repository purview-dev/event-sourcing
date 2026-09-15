using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.ChangeFeed;

/// <summary>
/// Used by the <see cref="IEventStore{T}"/> to notify <see cref="IAggregateChangeFeedProcessor"/>
/// and <see cref="IAggregateChangeFeedProcessor{T}"/> implementations of save and delete
/// operations.
/// </summary>
/// <typeparam name="T">The <see cref="IAggregate"/> type that was changed.</typeparam>
/// <remarks><para>
/// All registered <see cref="IAggregateChangeFeedProcessor"/>s are included, but
/// <see cref="IAggregateChangeFeedProcessor.CanProcess(IAggregate)"/> is called prior to calling the
/// appropriate methods.
/// </para>
/// <para>All registered <see cref="IAggregateChangeFeedNotifier{T}"/> are called the non-specific versions.</para></remarks>
public interface IAggregateChangeFeedNotifier<T>
	where T : class, IAggregate, new()
{
	/// <summary>
	/// Notify all interested processors that a save operation is about to begin.
	/// </summary>
	/// <param name="aggregate">The aggregate that is going to be saved.</param>
	/// <param name="isNew">Indicates if the aggregate is new.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	Task BeforeSaveAsync(T aggregate, bool isNew, CancellationToken cancellationToken = default);

	/// <summary>
	/// Notifies all interested processors that a delete operation is about to begin.
	/// </summary>
	/// <param name="aggregate">The aggregate that is going to be deleted.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	/// <remarks>This is not called if <see cref="EventStoreOperationContext.PermanentlyDelete"/> is set to true.</remarks>
	Task BeforeDeleteAsync(T aggregate, CancellationToken cancellationToken = default);

	/// <summary>
	/// Notifies all interested processors that a save operation has completed successfully.
	/// </summary>
	/// <param name="aggregate">The newly saved aggregate.</param>
	/// <param name="previousSavedVersion">The aggregate version prior to being saved, if the aggregate is new then this will be 0 (zero).</param>
	/// <param name="isNew">Indicates if the aggregate was new.</param>
	/// <param name="events">The <see cref="EventRecord"/>s that were saved as part of this operation.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	Task AfterSaveAsync(
		T aggregate,
		int previousSavedVersion,
		bool isNew,
		EventRecord[] events,
		CancellationToken cancellationToken = default
	);

	/// <summary>
	/// Notifies all interested processors that a delete operation has completed successfully.
	/// </summary>
	/// <param name="aggregate">The deleted aggregate.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	/// <remarks>This is not called if <see cref="EventStoreOperationContext.PermanentlyDelete"/> is set to true.</remarks>
	Task AfterDeleteAsync(T aggregate, CancellationToken cancellationToken = default);

	/// <summary>
	/// Notifies all interested processors that a failure occured during a save or delete operation.
	/// </summary>
	/// <param name="aggregate">The aggregate for the failed operation.</param>
	/// <param name="isDelete">If true, the operation was a delete, otherwise, it was a save operation.</param>
	/// <param name="exception">The exception that occurred during the operation.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	Task FailureAsync(T aggregate, bool isDelete, Exception exception, CancellationToken cancellationToken = default);
}
