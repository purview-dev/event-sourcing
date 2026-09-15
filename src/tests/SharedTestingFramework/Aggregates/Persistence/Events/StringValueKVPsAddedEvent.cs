using System.Text.Json.Serialization;
using Microsoft.Extensions.Primitives;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.Aggregates.Persistence.Events;

[EventContract]
public sealed record StringValueKVPsAddedEvent
{
	public static int SchemaVersion => 1;

	[JsonIgnore]
	public EventMetadata Metadata { get; init; }

	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Performance",
		"CA1819:Properties should not return arrays",
		Justification = "DTO"
	)]
	public KeyValuePair<string, StringValues>[] KVPs { get; set; } = default!;
}
