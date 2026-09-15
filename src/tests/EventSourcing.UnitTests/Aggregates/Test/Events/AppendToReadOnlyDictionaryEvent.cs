using System.Text.Json.Serialization;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.Aggregates.Test.Events;

[EventContract]
public sealed record AppendToReadOnlyDictionaryEvent
{
	public static int SchemaVersion => 1;

	[JsonIgnore]
	public EventMetadata Metadata { get; init; }

	public string Key { get; set; } = default!;

	public IEnumerable<string> Values { get; set; } = [];

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Key);
		if (Values is not null)
		{
			foreach (var value in Values)
				hash.Add(value);
		}
		return hash.ToHashCode();
	}
}
