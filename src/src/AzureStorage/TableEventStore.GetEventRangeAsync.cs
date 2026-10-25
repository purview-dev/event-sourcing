using System.Runtime.CompilerServices;
using Azure.Data.Tables;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.AzureStorage.Entities;

namespace Purview.EventSourcing.AzureStorage;

partial class TableEventStore<T>
{
	///<inheritdoc/>
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
				$"{nameof(versionTo)} ({versionTo}) must be greater than or equal to ${nameof(versionFrom)} ({versionFrom})."
			);

		var aggregateVersion = versionFrom;
		var entities = GetEventRangeEntitiesAsync(aggregateId, versionFrom, versionTo, cancellationToken);
		await foreach (var entity in entities)
		{
			var item = await DeserializeEventAsync(entity, aggregateVersion, cancellationToken);
			if (item != null)
				yield return (item.Value, entity.EventType);

			aggregateVersion++;
		}
	}

	internal async IAsyncEnumerable<EventEntity> GetEventRangeEntitiesAsync(
		string aggregateId,
		long versionFrom,
		long? versionTo,
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		// This query can't be done with LINQ, so don't try.
		var filter =
			$"({nameof(ITableEntity.PartitionKey)} eq '{aggregateId}') and (({nameof(ITableEntity.RowKey)} ge '{CreateEventRowKey(versionFrom)}') and ({nameof(ITableEntity.RowKey)} le '{CreateEventRowKey(versionTo ?? int.MaxValue)}'))";
		var query = _tableClient.QueryEnumerableAsync<EventEntity>(filter, cancellationToken: cancellationToken);
		await foreach (var eventEntity in query)
			yield return eventEntity;
	}

	async Task<EventRecord?> DeserializeEventAsync(
		EventEntity eventEntity,
		long aggregateVersion,
		CancellationToken cancellationToken
	)
	{
		cancellationToken.ThrowIfCancellationRequested();

		static UnknownEvent ReturnUnknownEvent(EventEntity eventEntity, long aggregateVersion) =>
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
			aggregateVersion,
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

				return new EventRecord(ReturnUnknownEvent(eventEntity, aggregateVersion), metadata);
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

			if (@event is Events.LargeEventPointerEvent blobPointer)
			{
				var blobName = GenerateEventBlobName(eventEntity.PartitionKey, eventEntity.RowKey);
				var exists = await _blobClient.ExistsAsync(blobName, cancellationToken);
				if (!exists)
				{
					_eventStoreTelemetry.SkippedMissingBlobEvent(
						eventEntity.PartitionKey,
						eventEntity.RowKey,
						blobPointer.SerializedEventType,
						blobName
					);
					return null;
				}

				var blobEventTypeName = _eventNameMapper.GetTypeName<T>(blobPointer.SerializedEventType);
				if (string.IsNullOrWhiteSpace(blobEventTypeName))
				{
					_eventStoreTelemetry.SkippedMissingBlobEventName(
						eventEntity.PartitionKey,
						eventEntity.RowKey,
						blobPointer.SerializedEventType,
						blobName
					);
					return new EventRecord(ReturnUnknownEvent(eventEntity, aggregateVersion), metadata);
					//throw new ArgumentNullException($"Unable to locate blob event type name {blobPointer.SerializedEventType}");
				}

				var blobEventType = EventTypeCache.GetOrAdd(
					blobEventTypeName,
					static name =>
						Type.GetType(name, throwOnError: false)
						?? throw new ApplicationException($"Unable to load event type: {name}")
				);
				if (blobEventType == null)
				{
					_eventStoreTelemetry.MissingBlobEventType(
						_aggregateTypeFullName,
						eventEntity.EventType,
						blobPointer.SerializedEventType,
						blobEventTypeName
					);
					return new EventRecord(ReturnUnknownEvent(eventEntity, aggregateVersion), metadata);
				}
				//throw new ArgumentNullException($"Unable to locate blob event type {blobEventTypeName}");

				var eventStream = await _blobClient.GetStreamAsync(blobName, cancellationToken);
				if (eventStream == null)
					return new EventRecord(ReturnUnknownEvent(eventEntity, aggregateVersion), metadata);

				using (eventStream)
				{
					// The blob holds the same envelope as the table would, so an oversized event is
					// erased by the same key and must be unprotected the same way.
					if (_payloadProtector is not null)
					{
						using StreamReader reader = new(eventStream);
						var blobPayload = await reader.ReadToEndAsync(cancellationToken);
						if (_payloadProtector.IsProtected(blobPayload))
						{
							var unprotectedBlob = await _payloadProtector.UnprotectAsync(
								blobPayload,
								cancellationToken
							);
							if (unprotectedBlob is null)
								return new EventRecord(ReturnUnknownEvent(eventEntity, aggregateVersion), metadata);

							var protectedEvent = DeserializeEvent(unprotectedBlob, blobEventType);

							return protectedEvent == null ? null : new EventRecord(protectedEvent, metadata);
						}

						var plainEvent = DeserializeEvent(blobPayload, blobEventType);

						return plainEvent == null ? null : new EventRecord(plainEvent, metadata);
					}

					var blobEvent = await DeserializeEventAsync(eventStream, blobEventType, cancellationToken);
					if (blobEvent == null)
						return null;

					// Apply upcasting chain when a registry is available.
					return new EventRecord(blobEvent, metadata);
				}
			}

			if (@event == null)
				return null;

			// Apply upcasting chain when a registry is available.
			return new EventRecord(@event, metadata);
		}
#pragma warning disable CA1031
		catch (Exception ex)
#pragma warning restore CA1031
		{
			_eventStoreTelemetry.EventDeserializationFailed(eventEntity.PartitionKey, _aggregateTypeFullName, ex);

			return new EventRecord(ReturnUnknownEvent(eventEntity, aggregateVersion), metadata);
		}
	}
}
