namespace Purview.EventSourcing;

/// <summary>
/// Marks an exception as an optimistic-concurrency conflict so framework helpers can distinguish
/// retryable write conflicts from other storage failures across every provider.
/// </summary>
/// <remarks>
/// A save reports a lost concurrency check as the <see cref="Failures.ConcurrencyConflict"/> result case
/// rather than throwing, so this marker covers the paths that still raise one - a caller's own
/// <c>ConcurrencyException</c>, or an operation outside the save contract - and lets a single retry policy
/// (see <see cref="ConcurrencyRetry.ExecuteAsync{TResult}"/>) recognize them without referencing provider
/// assemblies.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040:Avoid empty interfaces")]
public interface IConcurrencyConflict;
