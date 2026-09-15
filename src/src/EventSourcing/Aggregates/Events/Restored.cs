namespace Purview.EventSourcing.Aggregates.Events;

/// <summary>
/// Represents an <see cref="EventContractAttribute"/> event that tracks
/// the restoring of an <see cref="IAggregate"/> following a soft delete.
/// </summary>
[EventContract]
public sealed record Restored
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
