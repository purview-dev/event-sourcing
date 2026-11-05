using System.ComponentModel;
using Purview.EventSourcing.Aggregates;
using Purview.Results;

namespace Purview.EventSourcing.Failures;

/// <summary>
/// Widens a save failure into the delete and restore families, which are implemented on top of a save.
/// </summary>
/// <remarks>
/// Deleting and restoring append an event and go through the same save path, so every way that save can
/// fail has to reach the caller as the failure of the operation they actually asked for.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AggregateSaveFailureExtensions
{
	/// <summary>
	/// Converts a save failure into the equivalent delete failure.
	/// </summary>
	/// <typeparam name="T">The aggregate type.</typeparam>
	/// <param name="failure">The failure reported by the underlying save.</param>
	/// <returns>The same reason, expressed in the delete family.</returns>
	public static Result<SaveOutcome<T>, AggregateDeleteFailure> ToDeleteFailure<T>(this AggregateSaveFailure failure)
		where T : class, IAggregate, new() =>
		failure switch
		{
			// A save that reports the aggregate is already deleted is, to a delete, an already-deleted
			// aggregate rather than an obstacle.
			AggregateDeleted deleted => AggregateDeleteFailure.Failure<SaveOutcome<T>>(
				new AggregateAlreadyDeleted(deleted.AggregateType, deleted.AggregateId)
			),
			AggregateLocked locked => AggregateDeleteFailure.Failure<SaveOutcome<T>>(locked),
			ConcurrencyConflict conflict => AggregateDeleteFailure.Failure<SaveOutcome<T>>(conflict),
			AggregateInvalid invalid => AggregateDeleteFailure.Failure<SaveOutcome<T>>(invalid),
			_ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unhandled save failure."),
		};

	/// <summary>
	/// Converts a save failure into the equivalent restore failure.
	/// </summary>
	/// <typeparam name="T">The aggregate type.</typeparam>
	/// <param name="failure">The failure reported by the underlying save.</param>
	/// <returns>The same reason, expressed in the restore family.</returns>
	public static Result<SaveOutcome<T>, AggregateRestoreFailure> ToRestoreFailure<T>(this AggregateSaveFailure failure)
		where T : class, IAggregate, new() =>
		failure switch
		{
			AggregateLocked locked => AggregateRestoreFailure.Failure<SaveOutcome<T>>(locked),
			ConcurrencyConflict conflict => AggregateRestoreFailure.Failure<SaveOutcome<T>>(conflict),
			AggregateInvalid invalid => AggregateRestoreFailure.Failure<SaveOutcome<T>>(invalid),
			// A restore appends a Restored event, which is exactly what exempts the save from its
			// deleted-aggregate check, so AggregateDeleted cannot reach here.
			_ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unhandled save failure."),
		};
}
