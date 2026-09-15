using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Primitives;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Aggregates.Events.Upcasting;

namespace Purview.EventSourcing.Serialization;

public sealed class EventStoreSerializationHelpersTests
{
	[Test]
	public async Task SerializeAndDeserialize_GivenAggregateWithPrivateSetterAndGetterOnlyCollections_RestoresState()
	{
		string[] value = ["value-2", "value-3"];
		SerializerAggregate aggregate = new()
		{
			Details = new AggregateDetails { Id = "aggregate-1", CurrentVersion = 3 },
		};
		aggregate.Seed(
			"customer-1",
			[
				new KeyValuePair<string, StringValues>("single", "value-1"),
				new KeyValuePair<string, StringValues>("multi", value),
			],
			["item-1", "item-2"]
		);

		var json = EventStoreSerializationHelpers.Serialize(aggregate, aggregate.GetType());
		var roundTripped = EventStoreSerializationHelpers.Deserialize<SerializerAggregate>(json);

		await Assert.That(roundTripped).IsNotNull();
		await Assert.That(roundTripped!.Name).IsEqualTo("customer-1");
		await Assert.That(roundTripped.Details.Id).IsEqualTo("aggregate-1");
		await Assert.That(roundTripped.Details.CurrentVersion).IsEqualTo(3);
		await Assert.That(roundTripped.StringValuesDictionary["single"].ToString()).IsEqualTo("value-1");
		await Assert.That(roundTripped.StringValuesDictionary["multi"].ToArray<string>()).IsEquivalentTo(value);
		await Assert.That(roundTripped.Tags).IsEquivalentTo(["item-1", "item-2"]);
	}

	[Test]
	public async Task SerializeAndDeserialize_GivenEventWithMetadata_DoesNotSerializeMetadata()
	{
		SerializerEvent @event = new()
		{
			Metadata = new EventMetadata(
				AggregateVersion: 2,
				When: DateTimeOffset.UtcNow,
				SchemaVersion: 1,
				IdempotencyId: null,
				CorrelationId: "corr-1",
				CausationId: null,
				UserId: null
			),
			Value = "event-value",
		};

		var json = EventStoreSerializationHelpers.Serialize(@event, @event.GetType());
		var roundTripped = EventStoreSerializationHelpers.Deserialize<SerializerEvent>(json);

		await Assert.That(roundTripped).IsNotNull();
		await Assert.That(roundTripped!.Value).IsEqualTo("event-value");
		await Assert.That(roundTripped.Metadata).IsEqualTo(default(EventMetadata));

		using var document = JsonDocument.Parse(json);
		await Assert.That(document.RootElement.TryGetProperty("Metadata", out _)).IsFalse();
	}

	[Test]
	public async Task Serialize_GivenStringValues_WritesExpectedShapesAndRoundTrips()
	{
		var value = new[] { "one", "two" };
		var singleJson = EventStoreSerializationHelpers.Serialize(new StringValuesEnvelope { Value = "single" });
		var multiJson = EventStoreSerializationHelpers.Serialize(new StringValuesEnvelope { Value = value });
		var emptyJson = EventStoreSerializationHelpers.Serialize(
			new StringValuesEnvelope { Value = StringValues.Empty }
		);

		using var singleDocument = JsonDocument.Parse(singleJson);
		using var multiDocument = JsonDocument.Parse(multiJson);
		using var emptyDocument = JsonDocument.Parse(emptyJson);

		await Assert.That(singleDocument.RootElement.GetProperty("Value").ValueKind).IsEqualTo(JsonValueKind.String);
		await Assert.That(multiDocument.RootElement.GetProperty("Value").ValueKind).IsEqualTo(JsonValueKind.Array);
		await Assert.That(emptyDocument.RootElement.GetProperty("Value").ValueKind).IsEqualTo(JsonValueKind.Null);

		var single = EventStoreSerializationHelpers.Deserialize<StringValuesEnvelope>(singleJson);
		var multi = EventStoreSerializationHelpers.Deserialize<StringValuesEnvelope>(multiJson);
		var empty = EventStoreSerializationHelpers.Deserialize<StringValuesEnvelope>(emptyJson);

		await Assert.That(single!.Value.ToString()).IsEqualTo("single");
		await Assert.That(multi!.Value.ToArray<string>()).IsEquivalentTo(value);
		await Assert.That(empty!.Value.Count).IsEqualTo(0);
	}

	[Test]
	public async Task Deserialize_GivenLegacyEventJson_ProducesEventThatCanBeUpcast()
	{
		LegacySerializerEvent legacyEvent = new() { OldField = "legacy" };
		var json = EventStoreSerializationHelpers.Serialize(legacyEvent, legacyEvent.GetType());
		var deserialized = EventStoreSerializationHelpers.Deserialize<LegacySerializerEvent>(json)!;

		EventUpcasterRegistry registry = new([
			new EventUpcasterDescriptor<LegacySerializerEvent, CurrentSerializerEvent>(
				new LegacySerializerEventUpcaster()
			),
		]);

		var upcast = (CurrentSerializerEvent)registry.Upcast(deserialized);

		await Assert.That(upcast.NewField).IsEqualTo("legacy-upcast");
	}

	sealed class SerializerAggregate : AggregateBase
	{
		[JsonInclude]
		public string Name { get; private set; } = string.Empty;

		[JsonInclude]
		[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
		public Dictionary<string, StringValues> StringValuesDictionary { get; } = [];

		[JsonInclude]
		[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
		public List<string> Tags { get; } = [];

		public void Seed(
			string name,
			IEnumerable<KeyValuePair<string, StringValues>> stringValues,
			IEnumerable<string> tags
		)
		{
			Name = name;
			StringValuesDictionary.Clear();
			foreach (var kvp in stringValues)
				StringValuesDictionary[kvp.Key] = kvp.Value;

			Tags.Clear();
			Tags.AddRange(tags);
		}

		protected override void RegisterEvents() { }
	}

	[EventContract]
	sealed record SerializerEvent
	{
		public static int SchemaVersion => 1;

		[JsonIgnore]
		public EventMetadata Metadata { get; init; }

		public string Value { get; set; } = string.Empty;
	}

	sealed class StringValuesEnvelope
	{
		public StringValues Value { get; set; }
	}

	[EventContract]
	sealed record LegacySerializerEvent
	{
		public static int SchemaVersion => 1;

		[JsonIgnore]
		public EventMetadata Metadata { get; init; }

		public string OldField { get; set; } = string.Empty;
	}

	[EventContract]
	sealed record CurrentSerializerEvent
	{
		public static int SchemaVersion => 1;

		[JsonIgnore]
		public EventMetadata Metadata { get; init; }

		public string NewField { get; set; } = string.Empty;
	}

	sealed class LegacySerializerEventUpcaster : IEventUpcaster<LegacySerializerEvent, CurrentSerializerEvent>
	{
		public CurrentSerializerEvent Upcast(LegacySerializerEvent source) =>
			new() { NewField = source.OldField + "-upcast" };
	}
}
