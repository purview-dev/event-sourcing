using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.MongoDB.Events;

/// <summary>
/// Captures the saved, snapshot and current versions of an <see cref="IAggregate"/> at a point in time.
/// </summary>
/// <param name="SavedVersion">The last version persisted to the event stream.</param>
/// <param name="SnapshotVersion">The version captured by the most recent snapshot, or zero when none exists.</param>
/// <param name="CurrentVersion">The current version of the aggregate.</param>
public record struct AggregateVersionData(long SavedVersion, long SnapshotVersion, long CurrentVersion)
{
	/// <summary>
	/// Creates an <see cref="AggregateVersionData"/> instance from the details of an <see cref="IAggregate"/>.
	/// </summary>
	/// <param name="aggregate">The aggregate whose version data is captured.</param>
	/// <returns>An <see cref="AggregateVersionData"/> populated from <paramref name="aggregate"/>.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="aggregate"/> is <see langword="null"/>.</exception>
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
