using System.Runtime.CompilerServices;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.SqlServer.Events;

partial class SqlServerEventStore<T>
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
		long versionFrom,
		long? versionTo,
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
			var item = await DeserializeEventAsync(entity, cancellationToken);
			if (item != null)
				yield return (item.Value, entity.EventType!);
		}
	}

	/// <param name="eventRow"></param>
	/// <param name="cancellationToken"></param>
	async Task<EventRecord?> DeserializeEventAsync(
		SqlServerEventStoreClient.RowData eventRow,
		CancellationToken cancellationToken
	)
	{
		static UnknownEvent ReturnUnknownEvent(SqlServerEventStoreClient.RowData eventRow, long aggregateVersion) =>
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
			var payload = eventRow.Payload!;
			if (_payloadProtector is not null && _payloadProtector.IsProtected(payload))
			{
				var unprotected = await _payloadProtector.UnprotectAsync(payload, cancellationToken);
				if (unprotected is null)
					// The subject has been erased. Surfaced as an unknown event so replay continues and
					// the aggregate reports the gap, rather than failing the whole read.
					return new EventRecord(ReturnUnknownEvent(eventRow, eventRow.Version), metadata);

				payload = unprotected;
			}

			var @event = DeserializeEvent(payload, runtimeEventType);

			// Apply upcasting chain when a registry is available.
			if (@event != null && _eventUpcasterRegistry?.CanUpcast(@event) == true)
				@event = _eventUpcasterRegistry.Upcast(@event);

			if (@event == null)
				return null;

			// If the event is an IEvent, we can return it directly. Otherwise, we wrap it in an UnknownEvent.
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
