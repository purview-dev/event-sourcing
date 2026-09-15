using System.Text.Json.Serialization;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.Aggregates.Test.Events;

[EventContract]
public sealed record RecordEventTestEvent
{
	public static int SchemaVersion => 1;

	[JsonIgnore]
	public EventMetadata Metadata { get; init; }
}
