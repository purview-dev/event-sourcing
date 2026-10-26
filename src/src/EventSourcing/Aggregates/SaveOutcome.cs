namespace Purview.EventSourcing.Aggregates;

/// <summary>
/// Why a save operation did, or did not, write to the stream.
/// </summary>
public enum SaveOutcomeKind
{
	/// <summary>Events were appended to the stream.</summary>
	Written = 0,

	/// <summary>The aggregate had no unsaved events, so there was nothing to append.</summary>
	NoChanges = 1,

	/// <summary>
	/// An idempotency marker matched, so the events had already been appended by an earlier attempt.
	/// </summary>
	AlreadyApplied = 2,
}

/// <summary>
/// The successful result of a save, delete or restore operation.
/// </summary>
/// <typeparam name="TAggregate">The aggregate type the operation was performed on.</typeparam>
/// <param name="Aggregate">The aggregate the operation was performed on.</param>
/// <param name="Kind">Whether the operation wrote to the stream, and if not, why not.</param>
/// <remarks>
/// Success that wrote nothing is still success. The failure side of the result carries every reason an
/// operation did not succeed, so no caller has to infer one from a boolean.
/// </remarks>
public readonly record struct SaveOutcome<TAggregate>(TAggregate Aggregate, SaveOutcomeKind Kind)
	where TAggregate : IAggregate
{
	/// <summary>
	/// Indicates whether events were appended to the stream.
	/// </summary>
	public bool Written => Kind == SaveOutcomeKind.Written;
}
