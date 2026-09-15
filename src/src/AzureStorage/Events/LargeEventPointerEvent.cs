using System.Text.Json.Serialization;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.AzureStorage.Events;

/// <summary>
/// An event that points to a large event payload stored in blob storage.
/// </summary>
/// <remarks>
/// When a serialized event exceeds the maximum table entity size, it is written to blob storage and a
/// <see cref="LargeEventPointerEvent"/> is persisted in its place so the payload can be located on replay.
/// </remarks>
[EventContract]
public sealed record LargeEventPointerEvent
{
	/// <summary>
	/// Gets the schema version of this event.
	/// </summary>
	public static int SchemaVersion => 1;

	/// <summary>
	/// Gets or sets the name of the event type stored in the blob.
	/// </summary>
	public string SerializedEventType { get; set; } = default!;

	/// <summary>
	/// Gets the framework-managed metadata for this event.
	/// </summary>
	[JsonIgnore]
	public EventMetadata Metadata { get; init; }
}
