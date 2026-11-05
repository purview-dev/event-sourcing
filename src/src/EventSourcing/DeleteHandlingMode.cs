namespace Purview.EventSourcing;

/// <summary>
/// Controls how a soft-deleted aggregate is reported by get operations.
/// </summary>
/// <remarks>
/// The mode no longer decides whether an exception is thrown: a deleted aggregate is an expected
/// outcome, reported as <see cref="Failures.AggregateDeleted"/>. It decides only whether the caller
/// wants that state back instead.
/// </remarks>
public enum DeleteHandlingMode
{
	/// <summary>
	/// A deleted aggregate is reported as <see cref="Failures.AggregateDeleted"/>. This is the default.
	/// </summary>
	ReturnsFailure = 0,

	/// <summary>
	/// A deleted aggregate is returned in its deleted state.
	/// </summary>
	ReturnsAggregate,
}
