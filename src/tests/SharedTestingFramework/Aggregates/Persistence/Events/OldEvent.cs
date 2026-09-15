using System.Text.Json.Serialization;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.Aggregates.Persistence.Events;

[SentinelEvent(Justification = "This is an old event used for testing purposes.")]
[EventContract]
public sealed record OldEvent
{
	public static int SchemaVersion => 1;

	[JsonIgnore]
	public EventMetadata Metadata { get; init; }

	public Guid Value { get; set; }
}
