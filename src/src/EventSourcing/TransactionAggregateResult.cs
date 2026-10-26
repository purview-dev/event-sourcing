using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;

namespace Purview.EventSourcing;

/// <summary>
/// Represents the outcome of saving a single aggregate within a transaction.
/// </summary>
public sealed class TransactionAggregateResult(
	IAggregate aggregate,
	bool saved,
	bool skipped,
	Exception? error,
	AggregateSaveFailure? failure = null
)
{
	/// <summary>The aggregate that was processed.</summary>
	public IAggregate Aggregate { get; } = aggregate;

	/// <summary><see langword="true"/> when the aggregate was persisted.</summary>
	public bool Saved { get; } = saved;

	/// <summary><see langword="true"/> when the save was skipped (no unsaved events, or idempotency marker matched).</summary>
	public bool Skipped { get; } = skipped;

	/// <summary>The exception that caused the save to fail, if any.</summary>
	public Exception? Error { get; } = error;

	/// <summary>
	/// The save failure returned by the event store, if the save failed for a reason the caller can act on.
	/// </summary>
	/// <remarks>
	/// Expected failures - a lost concurrency check, a locked or deleted aggregate, a rejected validation -
	/// are returned by the store rather than thrown, so they arrive here and not in <see cref="Error"/>.
	/// </remarks>
	public AggregateSaveFailure? Failure { get; } = failure;
}
