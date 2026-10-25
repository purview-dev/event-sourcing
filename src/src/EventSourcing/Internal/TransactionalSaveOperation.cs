using System.ComponentModel;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.Internal;

/// <summary>
/// Describes the result of a transactional save and the follow-up work to run after commit or rollback.
/// </summary>
/// <typeparam name="T">The aggregate type.</typeparam>
/// <remarks>
/// Hidden from IntelliSense as this is framework plumbing rather than public API. The after-commit and
/// after-rollback callbacks are invoked by the transaction coordinator once the surrounding transaction
/// completes.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class TransactionalSaveOperation<T>(
	Result<SaveOutcome<T>, AggregateSaveFailure> result,
	Func<CancellationToken, Task>? afterCommit = null,
	Func<CancellationToken, Task>? afterRollback = null
)
	where T : class, IAggregate, new()
{
	static readonly Func<CancellationToken, Task> NoOp = _ => Task.CompletedTask;

	/// <summary>
	/// Gets the result of the save operation.
	/// </summary>
	public Result<SaveOutcome<T>, AggregateSaveFailure> Result { get; } = result;

	/// <summary>
	/// Gets a value indicating whether the save produced a write, or matched an idempotency marker for
	/// one an earlier attempt had already made.
	/// </summary>
	public bool Saved => Result.IsSuccess && Result.Value.Kind is not SaveOutcomeKind.NoChanges;

	/// <summary>
	/// Gets a value indicating whether the save appended nothing, either because there was nothing to
	/// append or because the events were already applied.
	/// </summary>
	public bool Skipped => Result.IsSuccess && Result.Value.Kind is not SaveOutcomeKind.Written;

	readonly Func<CancellationToken, Task> _afterCommit = afterCommit ?? NoOp;
	readonly Func<CancellationToken, Task> _afterRollback = afterRollback ?? NoOp;

	/// <summary>
	/// Runs the work associated with committing the transaction.
	/// </summary>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>A task representing the asynchronous operation.</returns>
	public Task AfterCommitAsync(CancellationToken cancellationToken = default) => _afterCommit(cancellationToken);

	/// <summary>
	/// Runs the work associated with rolling back the transaction.
	/// </summary>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>A task representing the asynchronous operation.</returns>
	public Task AfterRollbackAsync(CancellationToken cancellationToken = default) => _afterRollback(cancellationToken);
}
