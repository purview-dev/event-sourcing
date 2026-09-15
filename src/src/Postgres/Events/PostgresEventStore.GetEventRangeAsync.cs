using System.Runtime.CompilerServices;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.Postgres.Events;

partial class PostgresEventStore<T>
{
	/// <summary>
	/// Gets a range of events for a given aggregate, as specified by it's <paramref name="aggregateId"/>.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="versionFrom">The inclusive event number to start the range at.</param>
	/// <param name="versionTo">Optional, the inclusive event number to finish the range at.</param>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>If no <paramref name="versionTo"/> is specified all available events greater than <paramref name="versionFrom"/> are returned.</returns>
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
				$"{nameof(versionTo)} ({versionTo}) must be greater than or equal to {nameof(versionFrom)} ({versionFrom})."
			);

		var effectiveVersionTo = versionTo ?? int.MaxValue;
		var entities = _client.GetEventRangeAsync(
			aggregateId,
			_aggregateTypeShortName,
			versionFrom,
			effectiveVersionTo,
			cancellationToken
		);
		await foreach (var entity in entities)
		{
			var item = DeserializeEvent(entity);
			if (item != null)
				yield return (item.Value, entity.EventType!);
		}
	}

	/// <param name="eventRow"></param>
	EventRecord? DeserializeEvent(PostgresEventStoreClient.RowData eventRow)
	{
		static UnknownEvent ReturnUnknownEvent(PostgresEventStoreClient.RowData eventRow, int aggregateVersion) =>
			new()
			{
				SchemaVersion = eventRow.SchemaVersion,
				Metadata = new EventMetadata(
					aggregateVersion,
					eventRow.Timestamp,
					eventRow.SchemaVersion,
					eventRow.IdempotencyId,
					eventRow.CorrelationId,
					eventRow.CausationId,
					eventRow.UserId
				),
				Payload = eventRow.Payload,
			};

		EventMetadata metadata = new(
			eventRow.Version,
			eventRow.Timestamp,
			eventRow.SchemaVersion,
			eventRow.IdempotencyId,
			eventRow.CorrelationId,
			eventRow.CausationId,
			eventRow.UserId
		);

		try
		{
			var eventType = _eventNameMapper.GetTypeName<T>(eventRow.EventType!);
			if (eventType == null)
			{
				_eventStoreTelemetry.MissingEventType(_aggregateTypeFullName, eventRow.EventType!);

				return new EventRecord(ReturnUnknownEvent(eventRow, eventRow.Version), metadata);
			}

			var runtimeEventType = EventTypeCache.GetOrAdd(
				eventType,
				static name =>
					Type.GetType(name, throwOnError: false)
					?? throw new ApplicationException($"Unable to load event type: {name}")
			);
			var @event = DeserializeEvent(eventRow.Payload!, runtimeEventType);

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
			_eventStoreTelemetry.EventDeserializationFailed(eventRow.AggregateId, _aggregateTypeFullName, ex);

			return new EventRecord(ReturnUnknownEvent(eventRow, eventRow.Version), metadata);
		}
	}
}
