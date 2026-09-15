namespace Purview.EventSourcing.Aggregates.Events;

/// <summary>
/// Represents an <see cref="EventContractAttribute"/> event that is used in-place
/// when an existing event could not be deserialized.
/// </summary>
/// <remarks>
/// <para>
/// This can occur when the aggregate's schema has changed
/// and it no long requires an event type, so it's removed.
/// </para>
/// <para>However, the event data still exists in the underlying store.</para>
/// </remarks>
[EventContract]
[SentinelEvent(Justification = "Used when an existing event could not be deserialized.")]
public sealed record UnknownEvent
{
	/// <summary>
	/// Represents the serialized payload that was the original event.
	/// </summary>
	public string? Payload { get; init; }

	/// <summary>
	/// Gets the schema version of this event.
	/// </summary>
	public int SchemaVersion { get; init; } = 1;

	/// <summary>
	/// Gets the framework-managed metadata for this event.
	/// </summary>
	public EventMetadata Metadata { get; init; }
}
