using System.Text.Json.Serialization;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Aggregates.Events.Upcasting;
using Purview.EventSourcing.Services;

namespace Purview.EventSourcing;

partial class AggregateEventNameMapperTests
{
	[EventContract]
	sealed record LegacyEventV1
	{
		public static int SchemaVersion => 1;

		[JsonIgnore]
		public EventMetadata Metadata { get; init; }

		public string OldField { get; set; } = default!;
	}

	[EventContract]
	sealed record CurrentEventV2
	{
		public static int SchemaVersion => 2;

		[JsonIgnore]
		public EventMetadata Metadata { get; init; }

		public string NewField { get; set; } = default!;
	}

	sealed class LegacyEventV1ToCurrentEventV2Upcaster : IEventUpcaster<LegacyEventV1, CurrentEventV2>
	{
		public CurrentEventV2 Upcast(LegacyEventV1 source) => new() { NewField = source.OldField + "_upgraded" };
	}

	[Test]
	public async Task GetTypeName_GivenLegacyUpcasterSourceType_ResolvesStoredName()
	{
		// Arrange
		EventUpcasterDescriptor<LegacyEventV1, CurrentEventV2> upcaster = new(
			new LegacyEventV1ToCurrentEventV2Upcaster()
		);
		AggregateEventNameMapper mapper = new([upcaster]);
		var aggregateName = mapper.InitializeAggregate<CorrectlyNamedAggregate>();

		// The stored event name is what the store would have persisted when LegacyEventV1
		// was a current event type on this aggregate.
		var legacyName = TypeNameHelper.GetName(typeof(LegacyEventV1), "Event", true);
		if (legacyName != typeof(LegacyEventV1).FullName)
			legacyName = $"{aggregateName}.{legacyName}";

		// Act
		var result = mapper.GetTypeName<CorrectlyNamedAggregate>(legacyName);

		// Assert
		await Assert.That(result).IsEqualTo(typeof(LegacyEventV1).AssemblyQualifiedName);
	}

	[Test]
	public async Task GetTypeName_GivenNoUpcasterRegistered_ReturnsNull()
	{
		// Arrange
		var mapper = CreateMapper<CorrectlyNamedAggregate>();

		// Act
		var result = mapper.GetTypeName<CorrectlyNamedAggregate>("correctly-named.legacy-event-v1");

		// Assert
		await Assert.That(result).IsNull();
	}
}
