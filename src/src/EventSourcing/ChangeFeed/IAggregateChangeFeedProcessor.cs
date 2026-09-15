using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.ChangeFeed;

/// <summary>
/// Allows the implementor to respond to before and after events
/// of save, delete and failure operations.
/// </summary>
/// <remarks>All <see cref="IAggregate"/> types will be processed using this interface.
/// Use <see cref="CanProcess(IAggregate)"/> to indicate if you want to be included in a specific
/// aggregates notification process.</remarks>
public interface IAggregateChangeFeedProcessor
{
	/// <summary>
	/// Indicates if the <paramref name="aggregate"/> should be processed using the implementation.
	/// </summary>
	/// <param name="aggregate">The aggregate to test.</param>
	/// <returns>True to use the implementation to process notifications. Otherwise, false and no
	/// methods will be called.n</returns>
	/// <remarks>Has a default implementation that returns true.</remarks>
	bool CanProcess(IAggregate aggregate) => true;

	/// <summary>
	/// Called prior to the save operation being called on the aggregate.
	/// </summary>
	/// <param name="aggregate">The aggregate to be saved.</param>
	/// <param name="isNew">Indicates if the aggregate is new or it is an existing aggregate.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	/// <remarks>Returns a <see cref="Task.CompletedTask"/>.</remarks>
	Task BeforeSaveAsync(IAggregate aggregate, bool isNew, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	/// <summary>
	/// Called after the save operations has been successfully called on the aggregate.
	/// </summary>
	/// <param name="aggregate">The aggregate that was saved.</param>
	/// <param name="previousSavedVersion">The aggregate version prior to being saved, if the aggregate is new then this will be 0 (zero).</param>
	/// <param name="isNew">Indicates if the aggregate was new.</param>
	/// <param name="events">The <see cref="EventRecord"/>s that were saved as part of this operation.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	/// <remarks>Returns a <see cref="Task.CompletedTask"/>.</remarks>
	Task AfterSaveAsync(
		IAggregate aggregate,
		int previousSavedVersion,
		bool isNew,
		EventRecord[] events,
		CancellationToken cancellationToken = default
	) => Task.CompletedTask;

	/// <summary>
	/// Called before the delete operation has completed.
	/// </summary>
	/// <param name="aggregate">The aggregate to be deleted.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	/// <remarks>Returns a <see cref="Task.CompletedTask"/>.</remarks>
	Task BeforeDeleteAsync(IAggregate aggregate, CancellationToken cancellationToken = default) => Task.CompletedTask;

	/// <summary>
	/// Called after the delete operation has completed successfully.
	/// </summary>
	/// <param name="aggregate">The aggregate that was deleted.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	/// <remarks>Returns a <see cref="Task.CompletedTask"/>.</remarks>
	Task AfterDeleteAsync(IAggregate aggregate, CancellationToken cancellationToken = default) => Task.CompletedTask;

	/// <summary>
	/// Called if the operation failurd during a save or delete operation.
	/// </summary>
	/// <param name="aggregate">The aggregate for the failed operation.</param>
	/// <param name="isDelete">If true, the operation was a delete, otherwise, it was a save operation.</param>
	/// <param name="exception">The exception that occurred during the operation.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	/// <remarks>Returns a <see cref="Task.CompletedTask"/>.</remarks>
	Task FailureAsync(
		IAggregate aggregate,
		bool isDelete,
		Exception exception,
		CancellationToken cancellationToken = default
	);
}

/// <summary>
/// Allows the implementor to respond to before and after events
/// of save, delete and failure operations.
/// </summary>
/// <typeparam name="T">The specific <see cref="IAggregate"/> type used for notifications.</typeparam>
public interface IAggregateChangeFeedProcessor<T>
	where T : class, IAggregate, new()
{
	/// <summary>
	/// Called prior to the save operation being called on the aggregate.
	/// </summary>
	/// <param name="aggregate">The aggregate to be saved.</param>
	/// <param name="isNew">Indicates if the aggregate is new or it is an existing aggregate.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	/// <remarks>Returns a <see cref="Task.CompletedTask"/>.</remarks>
	Task BeforeSaveAsync(T aggregate, bool isNew, CancellationToken cancellationToken = default) => Task.CompletedTask;

	/// <summary>
	/// Called prior to the save operation being called on the aggregate.
	/// </summary>
	/// <param name="aggregate">The aggregate to be saved.</param>
	/// <param name="previousSavedVersion">The aggregate version prior to being saved, if the aggregate is new then this will be 0 (zero).</param>
	/// <param name="isNew">Indicates if the aggregate is new or it is an existing aggregate.</param>
	/// <param name="events">The <see cref="EventRecord"/>s that were saved as part of this operation.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	/// <remarks>Returns a <see cref="Task.CompletedTask"/>.</remarks>
	Task AfterSaveAsync(
		T aggregate,
		int previousSavedVersion,
		bool isNew,
		EventRecord[] events,
		CancellationToken cancellationToken = default
	) => Task.CompletedTask;

	/// <summary>
	/// Called before the delete operation has completed.
	/// </summary>
	/// <param name="aggregate">The aggregate to be deleted.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	/// <remarks>Returns a <see cref="Task.CompletedTask"/>.</remarks>
	Task BeforeDeleteAsync(T aggregate, CancellationToken cancellationToken = default) => Task.CompletedTask;

	/// <summary>
	/// Called after the delete operation has completed successfully.
	/// </summary>
	/// <param name="aggregate">The aggregate that was deleted.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	/// <remarks>Returns a <see cref="Task.CompletedTask"/>.</remarks>
	Task AfterDeleteAsync(T aggregate, CancellationToken cancellationToken = default) => Task.CompletedTask;

	/// <summary>
	/// Called if the operation failurd during a save or delete operation.
	/// </summary>
	/// <param name="aggregate">The aggregate for the failed operation.</param>
	/// <param name="isDelete">If true, the operation was a delete, otherwise, it was a save operation.</param>
	/// <param name="exception">The exception that occurred during the operation.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>An awaitable task.</returns>
	/// <remarks>Returns a <see cref="Task.CompletedTask"/>.</remarks>
	Task FailureAsync(T aggregate, bool isDelete, Exception exception, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;
}
