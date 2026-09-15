using System.Text.Json.Serialization;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Services;

namespace Purview.EventSourcing
{
	public partial class AggregateEventNameMapperTests
	{
		const string CorrectlyNamedAggregateName = "correctly-named";

		static AggregateEventNameMapper CreateMapper<T>()
			where T : class, IAggregate, new()
		{
			AggregateEventNameMapper? eventNameMapper = new();
			eventNameMapper.InitializeAggregate<T>();

			return eventNameMapper;
		}

		[EventContract]
		sealed record EventTypeEndingInEvent
		{
			public static int SchemaVersion => 1;

			[JsonIgnore]
			public EventMetadata Metadata { get; init; }
		}

		[EventContract]
		sealed record EventTypeNotEndingInEvent2
		{
			public static int SchemaVersion => 1;

			[JsonIgnore]
			public EventMetadata Metadata { get; init; }
		}

		sealed class CorrectlyNamedAggregate : AggregateBase
		{
			protected override void RegisterEvents() { }
		}
	}
}

#pragma warning disable IDE0130 // Namespace does not match folder structure
// This is for a specific set of tests
namespace Purview.Services.UserProfile.Aggregates.UserProfile.Events
#pragma warning restore IDE0130 // Namespace does not match folder structure
{
	[EventContract]
	public sealed record ClearProfileAttributesEvent
	{
		public static int SchemaVersion => 1;

		[JsonIgnore]
		public EventMetadata Metadata { get; init; }
	}

	[EventContract]
	public sealed record ClearRolesEvent
	{
		public static int SchemaVersion => 1;

		[JsonIgnore]
		public EventMetadata Metadata { get; init; }
	}
}
