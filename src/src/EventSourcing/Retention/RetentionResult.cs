namespace Purview.EventSourcing.Retention;

/// <summary>
/// What a retention run actually removed.
/// </summary>
/// <param name="StreamsExamined">How many streams the policy was evaluated against.</param>
/// <param name="StreamsPruned">How many streams had events removed.</param>
/// <param name="EventsRemoved">How many events were removed in total.</param>
/// <param name="Blockers">The streams that were not pruned, and why.</param>
/// <remarks>
/// The blockers are carried through from the preview the run was decided on, so the result answers
/// "what did this do" and "what is still being held back" together - the second being the question
/// an operator asks next.
/// </remarks>
public sealed record RetentionResult(
	int StreamsExamined,
	int StreamsPruned,
	long EventsRemoved,
	IReadOnlyList<RetentionBlocker> Blockers
);
