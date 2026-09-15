using System.Text.Json.Serialization;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.Aggregates;

public class InvalidEventTestAggregate : AggregateBase
{
	protected override void RegisterEvents()
	{
		Register<InvalidEventType>(Apply);
	}

	void Apply(InvalidEventType obj) { }
}

[EventContract]
public sealed record InvalidEventType
{
	public static int SchemaVersion => 1;

	[JsonIgnore]
	public EventMetadata Metadata { get; init; }
}
