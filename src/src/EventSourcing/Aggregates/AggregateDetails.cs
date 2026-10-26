using Purview.EventSourcing.Aggregates.Exceptions;

namespace Purview.EventSourcing.Aggregates;

/// <summary>
/// Represents the details of the aggregate.
/// </summary>
/// <remarks>Values in this class should not be modified manually.</remarks>
public sealed class AggregateDetails : ICloneable
{
	string _id = default!;

	/// <summary>
	/// Gets the Id of the aggregate.
	/// </summary>
	/// <remarks>Once the id is set, it cannot be changed.</remarks>
	public string Id
	{
		get => _id;
		set
		{
			if (Comparer<string>.Default.Compare(_id, value) == 0)
				return;

			if (!string.IsNullOrWhiteSpace(_id))
				throw new IdAlreadySetException();

			_id = value;
		}
	}

	/// <summary>
	/// The version of the snapshot that has been saved, if there are any events that are un-saved, or the snapshot doesn't
	/// contain more recent events then this value may not reflect the latest version of the aggregate.
	/// </summary>
	public long SnapshotVersion { get; set; }

	/// <summary>
	/// The version of this instance that has been saved to the <see cref="IEventStore{T}"/>.
	/// </summary>
	/// <remarks>
	/// <para>A value of 0 (zero) indicates that this is a new aggregate.</para>
	/// <para>
	/// You can think of this value as the latest saved version. Unlike the <see cref="SnapshotVersion"/>, this
	/// represents all of the persisted events.
	/// </para>
	/// </remarks>
	public long SavedVersion { get; set; }

	/// <summary>
	/// The current version of this instance of the <see cref="IAggregate"/>,
	/// taking into account any un-saved events.
	/// </summary>
	/// <remarks>While the events maybe unsaved, they are still applied and therefore
	/// used to update this value.</remarks>
	public long CurrentVersion { get; set; }

	/// <summary>
	/// Indicates if the aggregate is in a deleted state.
	/// </summary>
	public bool IsDeleted { get; set; }

	/// <summary>
	/// The <see cref="DateTimeOffset"/> the aggregate had it's first event applied.
	/// </summary>
	public DateTimeOffset Created { get; set; }

	/// <summary>
	/// The <see cref="DateTimeOffset"/> the aggregate had it's last event applied.
	/// </summary>
	public DateTimeOffset Updated { get; set; }

	/// <summary>
	/// Get's the Etag that corresponds to the latest serialized version.
	/// </summary>
	/// <remarks>
	/// This can be useful for caching, including the use of If-Modified headers
	/// when using HTTP/S clients.
	/// </remarks>
	public string? Etag { get; set; }

	/// <summary>
	/// Determines if this version of this aggregate is locked from saving, deleting or caching.
	/// </summary>
	public bool Locked
	{
		get;
		set
		{
			if (field && !value)
				throw new LockedException(Id, "This aggregate is locked, changing the locked state is not permitted.");

			field = value;
		}
	}

	/// <summary>
	/// Clones the <see cref="AggregateDetails"/> class to a new instance.
	/// </summary>
	/// <returns>A cloned instance of the current <see cref="AggregateDetails"/>.</returns>
	public object Clone() =>
		new AggregateDetails
		{
			_id = _id,
			Created = Created,
			Updated = Updated,
			SnapshotVersion = SnapshotVersion,
			SavedVersion = SavedVersion,
			CurrentVersion = CurrentVersion,
			IsDeleted = IsDeleted,
			Etag = Etag,
			Locked = Locked,
		};

	/// <summary>
	/// Generates a hash-code based on the properties of the <see cref="AggregateDetails"/>.
	/// </summary>
	public override int GetHashCode() =>
		HashCode.Combine(Id, Created, SnapshotVersion, SavedVersion, CurrentVersion, IsDeleted, Etag, Locked);
}
