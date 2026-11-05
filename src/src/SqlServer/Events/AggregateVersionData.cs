using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.SqlServer.Events;

/// <summary>
/// Captures the versioning state of an aggregate at a point in time.
/// </summary>
/// <remarks>
/// Represents the aggregate's <see cref="AggregateDetails.SavedVersion"/>,
/// snapshot version, and current version.
/// </remarks>
/// <param name="SavedVersion">The version of the aggregate persisted in the event store.</param>
/// <param name="SnapshotVersion">The version of the aggregate captured in the latest snapshot.</param>
/// <param name="CurrentVersion">The current version of the aggregate, including unsaved events.</param>
public record struct AggregateVersionData(long SavedVersion, long SnapshotVersion, long CurrentVersion)
{
	/// <summary>
	/// Creates an <see cref="AggregateVersionData"/> instance from the specified aggregate's details.
	/// </summary>
	/// <param name="aggregate">The aggregate whose version state should be captured.</param>
	/// <returns>An <see cref="AggregateVersionData"/> describing the aggregate's version state.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="aggregate"/> is <see langword="null"/>.</exception>
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
