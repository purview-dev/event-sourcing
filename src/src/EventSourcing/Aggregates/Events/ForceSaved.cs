namespace Purview.EventSourcing.Aggregates.Events;

/// <summary>
/// Represents an <see cref="EventContractAttribute"/> event that forces
/// the saving an <see cref="IAggregate"/> when <see cref="IEventStore{T}.SaveAsync(T, EventStoreOperationContext?, CancellationToken)"/>
/// is called.
/// </summary>
[EventContract]
public sealed record ForceSaved
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
