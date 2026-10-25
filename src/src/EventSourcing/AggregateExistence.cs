namespace Purview.EventSourcing;

/// <summary>
/// Whether an aggregate's stream exists, whether it is deleted, and the version it is at.
/// </summary>
/// <param name="Status">Whether the aggregate exists, and in what state.</param>
/// <param name="Version">The stream version when the aggregate exists, otherwise null.</param>
/// <remarks>
/// There is deliberately no conversion to <see cref="bool"/>: an aggregate that exists in a deleted state is
/// not the same answer as one that exists, and a single boolean cannot say which.
/// </remarks>
[System.Diagnostics.DebuggerStepThrough]
public readonly record struct AggregateExistence(ExistenceStatus Status, long? Version)
{
	/// <summary>
	/// Gets a value indicating whether the aggregate exists at all, deleted or not.
	/// </summary>
	public bool DoesExist => Status is ExistenceStatus.Exists or ExistenceStatus.ExistsInDeletedState;

	/// <summary>
	/// Gets a value indicating whether the aggregate exists and is in a deleted state.
	/// </summary>
	public bool IsDeleted => Status is ExistenceStatus.ExistsInDeletedState;

	/// <summary>
	/// An aggregate that does not exist.
	/// </summary>
	public static AggregateExistence DoesNotExist => new(ExistenceStatus.DoesNotExist, null);

	/// <summary>
	/// An aggregate that exists and is not deleted.
	/// </summary>
	public static AggregateExistence Exists => new(ExistenceStatus.Exists, null);

	/// <summary>
	/// An aggregate that exists in a deleted state.
	/// </summary>
	public static AggregateExistence ExistsInDeletedState => new(ExistenceStatus.ExistsInDeletedState, null);
}
