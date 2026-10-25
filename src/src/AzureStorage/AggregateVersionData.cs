using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.AzureStorage;

/// <summary>
/// Describes the saved, snapshot, and current versions of an <see cref="IAggregate"/>.
/// </summary>
/// <param name="SavedVersion">The last version persisted to the event store.</param>
/// <param name="SnapshotVersion">The version covered by the most recent snapshot.</param>
/// <param name="CurrentVersion">The current version of the aggregate.</param>
public record struct AggregateVersionData(long SavedVersion, long SnapshotVersion, long CurrentVersion)
{
	/// <summary>
	/// Creates an <see cref="AggregateVersionData"/> from the details of the given <paramref name="aggregate"/>.
	/// </summary>
	/// <param name="aggregate">The <see cref="IAggregate"/> to read the version details from.</param>
	/// <returns>An <see cref="AggregateVersionData"/> populated from the aggregate's details.</returns>
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
