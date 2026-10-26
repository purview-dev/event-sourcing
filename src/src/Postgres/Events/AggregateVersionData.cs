using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Postgres.Events;

/// <summary>
/// Captures the saved, snapshot, and current versions of an aggregate after events are applied.
/// </summary>
/// <param name="SavedVersion">The last version that was persisted to the event store.</param>
/// <param name="SnapshotVersion">The version captured by the latest snapshot, or zero when no snapshot exists.</param>
/// <param name="CurrentVersion">The current version of the aggregate.</param>
public record struct AggregateVersionData(long SavedVersion, long SnapshotVersion, long CurrentVersion)
{
	/// <summary>
	/// Creates version data from the aggregate's current details.
	/// </summary>
	/// <param name="aggregate">The aggregate whose version details are captured.</param>
	/// <returns>The version data describing <paramref name="aggregate"/>.</returns>
	/// <exception cref="ArgumentNullException">When <paramref name="aggregate"/> is <see langword="null"/>.</exception>
	public static AggregateVersionData Create(IAggregate aggregate)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		return new()
		{
			SavedVersion = aggregate.Details.SavedVersion,
			SnapshotVersion = aggregate.Details.SnapshotVersion,
			CurrentVersion = aggregate.Details.CurrentVersion,
		};
	}
}
