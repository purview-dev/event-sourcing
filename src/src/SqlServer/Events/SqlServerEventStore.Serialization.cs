using Purview.EventSourcing.Serialization;

namespace Purview.EventSourcing.SqlServer.Events;

partial class SqlServerEventStore<T>
{
	static object? DeserializeEvent(string eventContent, Type eventType) =>
		EventStoreSerializationHelpers.Deserialize(eventContent, eventType);

	static string SerializeSnapshot(T aggregate) =>
		EventStoreSerializationHelpers.Serialize(aggregate, aggregate.GetType());

	static string SerializeEvent(object @event) => EventStoreSerializationHelpers.Serialize(@event, @event.GetType());

	static T DeserializeSnapshot(string aggregateContent) =>
		EventStoreSerializationHelpers.Deserialize<T>(aggregateContent)!;
}
