using System.Runtime.CompilerServices;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.MongoDB.Events.Entities;

namespace Purview.EventSourcing.MongoDB;

partial class MongoDBEventStore<T>
{
	/// <summary>
	/// Gets a range of <see cref="EventContractAttribute"/> events for a given aggregate, as specified by it's <paramref name="aggregateId"/>.
	/// </summary>
	/// <param name="aggregateId">The id of the <see cref="Interfaces.Aggregates.IAggregate"/>.</param>
	/// <param name="versionFrom">The inclusive event number to start the range at.</param>
	/// <param name="versionTo">Optional, the inclusive event number to finish the range at.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>If no <paramref name="versionFrom"/> is specified all available events greater than <paramref name="versionFrom"/> are returned.</returns>
	public async IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync(
		string aggregateId,
		int versionFrom,
		int? versionTo,
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId, nameof(aggregateId));
		if (versionFrom < 1)
			throw new ArgumentOutOfRangeException(
				nameof(versionFrom),
				versionFrom,
				$"{nameof(versionFrom)} must be greater than 0."
			);

		if (versionTo < versionFrom)
			throw new ArgumentOutOfRangeException(
				nameof(versionTo),
				versionTo.Value,
				$"{nameof(versionTo)} ({versionTo}) must be greater than or equal to ${nameof(versionFrom)} ({versionFrom})."
			);

		var entities = GetEventRangeEntitiesAsync(aggregateId, versionFrom, versionTo, cancellationToken);
		await foreach (var entity in entities)
		{
			var item = DeserializeEvent(entity);
			if (item != null)
				yield return (item.Value, entity.EventType);
		}
	}

	internal async IAsyncEnumerable<EventEntity> GetEventRangeEntitiesAsync(
		string aggregateId,
		int versionFrom,
		int? versionTo,
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		versionTo ??= int.MaxValue;

		var query = _eventClient.GetQueryEnumerableAsync<EventEntity>(
			m =>
				m.AggregateId == aggregateId
				&& m.EntityType == EntityTypes.EventType
				&& m.Version >= versionFrom
				&& m.Version <= versionTo,
			orderByClause: m => m.OrderBy(e => e.Version),
			cancellationToken: cancellationToken
		);

		await foreach (var eventEntity in query)
			yield return eventEntity;
	}

	/// <param name="eventEntity"></param>
	EventRecord? DeserializeEvent(EventEntity eventEntity)
	{
		static UnknownEvent ReturnUnknownEvent(EventEntity eventEntity, int aggregateVersion) =>
			new()
			{
				SchemaVersion = eventEntity.SchemaVersion,
				Metadata = new EventMetadata(
					aggregateVersion,
					eventEntity.Timestamp!.Value,
					eventEntity.SchemaVersion,
					eventEntity.IdempotencyId,
					eventEntity.CorrelationId,
					eventEntity.CausationId,
					eventEntity.UserId
				),
				Payload = eventEntity.Payload,
			};

		EventMetadata metadata = new(
			eventEntity.Version,
			eventEntity.Timestamp!.Value,
			eventEntity.SchemaVersion,
			eventEntity.IdempotencyId,
			eventEntity.CorrelationId,
			eventEntity.CausationId,
			eventEntity.UserId
		);

		try
		{
			var eventType = _eventNameMapper.GetTypeName<T>(eventEntity.EventType);
			if (eventType == null)
			{
				_eventStoreTelemetry.MissingEventType(_aggregateTypeFullName, eventEntity.EventType);

				return new EventRecord(ReturnUnknownEvent(eventEntity, eventEntity.Version), metadata);
			}

			var runtimeEventType = EventTypeCache.GetOrAdd(
				eventType,
				static name =>
					Type.GetType(name, throwOnError: false)
					?? throw new ApplicationException($"Unable to load event type: {name}")
			);
			var @event = DeserializeEvent(eventEntity.Payload, runtimeEventType);

			// Apply upcasting chain when a registry is available.
			if (@event != null && _eventUpcasterRegistry?.CanUpcast(@event) == true)
				@event = _eventUpcasterRegistry.Upcast(@event);

			if (@event == null)
				return null;

			return new EventRecord(@event, metadata);
		}
#pragma warning disable CA1031
		catch (Exception ex)
#pragma warning restore CA1031
		{
			_eventStoreTelemetry.EventDeserializationFailed(eventEntity.AggregateId, _aggregateTypeFullName, ex);

			return new EventRecord(ReturnUnknownEvent(eventEntity, eventEntity.Version), metadata);
		}
	}
}
