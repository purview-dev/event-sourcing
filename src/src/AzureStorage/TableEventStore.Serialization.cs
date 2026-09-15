using Purview.EventSourcing.Serialization;

namespace Purview.EventSourcing.AzureStorage;

partial class TableEventStore<T>
{
	static object? DeserializeEvent(string eventContent, Type eventType) =>
		EventStoreSerializationHelpers.Deserialize(eventContent, eventType);

	static async Task<object?> DeserializeEventAsync(
		Stream eventStream,
		Type eventType,
		CancellationToken cancellationToken
	) => await EventStoreSerializationHelpers.DeserializeAsync(eventStream, eventType, cancellationToken);

	internal static string SerializeSnapshot(T aggregate) =>
		EventStoreSerializationHelpers.Serialize(aggregate, aggregate.GetType());

	internal static string SerializeEvent(object @event) =>
		EventStoreSerializationHelpers.Serialize(@event, @event.GetType());

	static T DeserializeSnapshot(string aggregateContent) =>
		EventStoreSerializationHelpers.Deserialize<T>(aggregateContent)!;
}
