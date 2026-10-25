namespace Purview.EventSourcing;

/// <summary>
/// Defines states for working out if an aggregate exists.
/// </summary>
public enum ExistenceStatus
{
	/// <summary>
	/// The aggregate does not exist.
	/// </summary>
	DoesNotExist,

	/// <summary>
	/// The aggregate exists, and is not in a deleted state.
	/// </summary>
	Exists,

	/// <summary>
	/// The aggregate exists, but is in a deleted state.
	/// </summary>
	ExistsInDeletedState,
}
