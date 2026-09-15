namespace Purview.EventSourcing.Aggregates.Events;

/// <summary>
/// Represents an <see cref="EventContractAttribute"/> event that tracks
/// the soft-deleting of an <see cref="IAggregate"/>.
/// </summary>
[EventContract]
public sealed record Deleted
{
	/// <summary>
	/// Gets the schema version of this event.
	/// </summary>
	public static int SchemaVersion => 1;

	/// <summary>
	/// Gets the framework-managed metadata for this event.
	/// </summary>
	public EventMetadata Metadata { get; init; }
}
