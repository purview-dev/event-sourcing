namespace Purview.EventSourcing.Retention;

/// <summary>
/// Why a stream cannot be pruned.
/// </summary>
public enum RetentionBlockerReason
{
	/// <summary>No snapshot covers the prune point, and the policy requires one.</summary>
	NoCoveringSnapshot = 0,

	/// <summary>The events that would be removed are newer than the policy's minimum age.</summary>
	TooRecent = 1,

	/// <summary>
	/// A subscription has not read past the prune point, so pruning would leave it permanently
	/// unable to catch up.
	/// </summary>
	SubscriptionBehind = 2,

	/// <summary>The stream is too short for the retained-version margin to leave anything.</summary>
	NothingToRemove = 3,
}

/// <summary>
/// One stream that would not be pruned, and why.
/// </summary>
/// <param name="AggregateId">The stream.</param>
/// <param name="Reason">Why it is blocked.</param>
/// <param name="Detail">Context an operator can act on - the lagging subscription's name, say.</param>
public sealed record RetentionBlocker(string AggregateId, RetentionBlockerReason Reason, string? Detail = null);

/// <summary>
/// What a retention run would do, without doing it.
/// </summary>
/// <remarks>
/// The preview exists so a policy can be found to be wrong <em>before</em> it costs anything. A
/// blocker is not a failure: it is the expected answer for most streams most of the time, and the
/// list is how an operator learns that, say, every stream is held back by one lagging subscription.
/// </remarks>
/// <param name="StreamsExamined">How many streams the policy was evaluated against.</param>
/// <param name="EventsRemovable">How many events would be removed in total.</param>
/// <param name="Eligible">The streams that would be pruned, and to which version.</param>
/// <param name="Blockers">The streams that would not be pruned, and why.</param>
public sealed record RetentionPreview(
	int StreamsExamined,
	long EventsRemovable,
	IReadOnlyList<RetentionCandidate> Eligible,
	IReadOnlyList<RetentionBlocker> Blockers
);

/// <summary>
/// A stream that would be pruned.
/// </summary>
/// <param name="AggregateId">The stream.</param>
/// <param name="PruneThroughVersion">The highest version that would be removed, inclusive.</param>
/// <param name="EventsRemovable">How many events that accounts for.</param>
public sealed record RetentionCandidate(string AggregateId, long PruneThroughVersion, long EventsRemovable);
