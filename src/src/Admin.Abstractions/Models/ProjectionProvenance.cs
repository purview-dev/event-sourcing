namespace Purview.EventSourcing.Admin.Models;

/// <summary>
/// Describes how a projection was produced from an aggregate event stream.
/// </summary>
/// <param name="AppliedCount">The number of events applied while producing the projection.</param>
/// <param name="SkippedCount">The number of events skipped while producing the projection.</param>
/// <param name="AppliedVersions">The stream versions of the events that were applied.</param>
/// <param name="SkippedVersions">The stream versions of the events that were skipped.</param>
/// <param name="Reason">A human-readable description of why events were skipped, when applicable.</param>
public sealed record ProjectionProvenance(
	int AppliedCount,
	int SkippedCount,
	IReadOnlyList<long> AppliedVersions,
	IReadOnlyList<long> SkippedVersions,
	string Reason
);
