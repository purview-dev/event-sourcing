using Purview.Telemetry;

namespace Purview.EventSourcing.MongoDB.Events;

/// <summary>
/// Telemetry contract for the <see cref="MongoDBEventStore{T}"/> implementation.
/// </summary>
/// <remarks>
/// Source-generated logging instrumentation used to trace aggregate reads, saves, deletes, restores and
/// cache operations, as well as to report failures such as deserialization errors, storage commit failures
/// and missing event types.
/// </remarks>
[Logger]
public interface IMongoDBEventStoreTelemetry
{
	/// <summary>
	/// Logs that an aggregate was retrieved from the distributed cache.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	[Debug]
	void AggregateRetrievedFromCache(string aggregateId, string aggregateTypeFullName);

	/// <summary>
	/// Logs the start of a get aggregate operation.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	[Debug]
	void GetAggregateStart(string aggregateId, string aggregateTypeFullName);

	/// <summary>
	/// Logs that getting an aggregate at a specific version failed.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="specificVersion">The version the aggregate was requested at.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Error]
	void GetAggregateAtSpecificVersionFailed(
		string aggregateId,
		string aggregateTypeFullName,
		long specificVersion,
		Exception exception
	);

	/// <summary>
	/// Logs that an aggregate was reconstituted by replaying its events.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	/// <param name="eventCount">The number of events replayed.</param>
	/// <param name="versionData">The version data captured after replay.</param>
	[Debug]
	void ReconstitutedAggregateFromEvents(
		string aggregateId,
		string aggregateTypeFullName,
		string aggregateType,
		int eventCount,
		AggregateVersionData versionData
	);

	/// <summary>
	/// Logs the start of a get aggregate at specific version operation.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="specificVersion">The version the aggregate is requested at.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	[Debug]
	void GetAggregateAtSpecificVersionStart(string aggregateId, long specificVersion, string aggregateTypeFullName);

	/// <summary>
	/// Logs that a save operation contained no changes to persist.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[Debug]
	void SaveContainedNoChanges(string aggregateId, string aggregateTypeFullName, string aggregateType);

	/// <summary>
	/// Logs that an event was skipped because its type is unknown to the aggregate.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	/// <param name="eventType">The serialized name of the event that was skipped.</param>
	/// <param name="aggregateVersion">The aggregate version the event was recorded at.</param>
	[Warning]
	void SkippedUnknownEvent(
		string aggregateId,
		string aggregateTypeFullName,
		string aggregateType,
		string eventType,
		long aggregateVersion
	);

	/// <summary>
	/// Logs that an event could not be applied to the aggregate.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	/// <param name="eventType">The serialized name of the event.</param>
	/// <param name="eventTypeFullName">The full name of the event type.</param>
	/// <param name="aggregateVersion">The aggregate version the event was recorded at.</param>
	[Warning]
	void CannotApplyEvent(
		string aggregateId,
		string aggregateTypeFullName,
		string aggregateType,
		string eventType,
		string eventTypeFullName,
		long aggregateVersion
	);

	/// <summary>
	/// Logs the completion of a get aggregate operation.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="elapsedMilliseconds">The elapsed time of the operation in milliseconds.</param>
	[Debug]
	void GetAggregateComplete(string aggregateId, string aggregateTypeFullName, long elapsedMilliseconds);

	/// <summary>
	/// Logs that deserializing an aggregate snapshot failed.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Error]
	void SnapshotDeserializationFailed(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that reading from the distributed cache failed.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Warning]
	void CacheGetFailure(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that updating the distributed cache failed.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Warning]
	void CacheUpdateFailure(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that a large event is being written.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="blobName">The name of the blob the event payload is written to.</param>
	/// <param name="length">The length of the event payload.</param>
	/// <param name="fullName">The full name of the event type.</param>
	[Debug]
	void WritingLargeEvent(string aggregateId, string blobName, long length, string fullName);

	/// <summary>
	/// Logs that an aggregate was soft-deleted.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[Debug]
	void AggregateDeleted(string aggregateId, string aggregateTypeFullName, string aggregateType);

	/// <summary>
	/// Logs that a previously deleted aggregate was restored.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[Debug]
	void AggregateRestored(string aggregateId, string aggregateTypeFullName, string aggregateType);

	/// <summary>
	/// Logs that a save operation was called.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[Debug]
	void SaveCalled(string aggregateId, string aggregateTypeFullName, string aggregateType);

	/// <summary>
	/// Logs that a save failed at the storage layer.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Error]
	void SaveFailedAtStorage(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that the events of a save operation were already applied.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="idempotencyId">The idempotency identifier of the operation.</param>
	[Debug]
	void EventsAlreadyApplied(string aggregateId, string idempotencyId);

	/// <summary>
	/// Logs that a save operation failed.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Error]
	void SaveFailed(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that the runtime event type for a serialized event type could not be resolved.
	/// </summary>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="eventType">The serialized name of the event type.</param>
	[Warning]
	void MissingEventType(string aggregateTypeFullName, string eventType);

	/// <summary>
	/// Logs the completion of a get aggregate at specific version operation.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="specificVersion">The version the aggregate was requested at.</param>
	/// <param name="elapsedMilliseconds">The elapsed time of the operation in milliseconds.</param>
	[Debug]
	void GetAggregateAtSpecificVersionComplete(
		string aggregateId,
		string aggregateTypeFullName,
		long specificVersion,
		long elapsedMilliseconds
	);

	/// <summary>
	/// Logs that an event referencing a missing blob was skipped.
	/// </summary>
	/// <param name="partitionKey">The partition key of the event record.</param>
	/// <param name="rowKey">The row key of the event record.</param>
	/// <param name="serializedEventType">The serialized name of the event type.</param>
	/// <param name="blobName">The name of the missing blob.</param>
	[Warning]
	void SkippedMissingBlobEvent(string partitionKey, string rowKey, string serializedEventType, string blobName);

	/// <summary>
	/// Logs that the blob event type could not be resolved.
	/// </summary>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="eventType">The serialized name of the event type.</param>
	/// <param name="serializedEventType">The serialized name of the blob event type.</param>
	/// <param name="blobEventTypeName">The name of the blob event type.</param>
	[Warning]
	void MissingBlobEventType(
		string aggregateTypeFullName,
		string eventType,
		string serializedEventType,
		string blobEventTypeName
	);

	/// <summary>
	/// Logs that removing an entry from the distributed cache failed.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Warning]
	void CacheRemovalFailure(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that deserializing an event failed.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Warning]
	void EventDeserializationFailed(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that an aggregate was saved successfully.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="eventCount">The number of events persisted.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[Debug]
	void SavedAggregate(string aggregateId, string aggregateTypeFullName, int eventCount, string aggregateType);

	/// <summary>
	/// Logs that a get aggregate operation failed.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Error]
	void GetAggregateFailed(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that a stream version record expected to exist was not found.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="aggregateTypeName">The short name of the aggregate type.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	[Warning]
	void StreamVersionExpectedToExistButNotFound(
		string aggregateId,
		string aggregateTypeName,
		string aggregateTypeFullName
	);

	/// <summary>
	/// Logs that no stream version record was found for an aggregate.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	[Debug]
	void StreamVersionNotFound(string aggregateId);

	/// <summary>
	/// Logs that a stream version record was found for an aggregate.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="streamVersion">The saved version of the aggregate.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	/// <param name="isDeleted">Whether the aggregate is soft-deleted.</param>
	[Debug]
	void StreamVersionFound(string aggregateId, long streamVersion, string aggregateType, bool isDeleted);

	/// <summary>
	/// Logs that reading the stream version record failed.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Error]
	void GetStreamVersionFailed(string aggregateId, Exception exception);

	/// <summary>
	/// Logs the start of a get stream version operation.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	[Debug]
	void GetStreamVersionStart(string aggregateId);

	/// <summary>
	/// Logs the completion of a get stream version operation.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="elapsedMilliseconds">The elapsed time of the operation in milliseconds.</param>
	[Debug]
	void GetStreamVersionComplete(string aggregateId, long elapsedMilliseconds);

	/// <summary>
	/// Logs that a permanent delete was requested.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	[Debug]
	void PermanentDeleteRequested(string aggregateId);

	/// <summary>
	/// Logs that a permanent delete failed.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Error]
	void PermanentDeleteFailed(string aggregateId, Exception exception);

	/// <summary>
	/// Logs that a permanent delete completed.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	[Debug]
	void PermanentDeleteComplete(string aggregateId);

	/// <summary>
	/// Logs that reading an idempotency marker failed.
	/// </summary>
	/// <param name="aggregateId">The identifier of the aggregate.</param>
	/// <param name="idempotencyId">The idempotency identifier of the operation.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Error]
	void GetIdempotencyMarkerFailed(string aggregateId, string idempotencyId, Exception exception);
}
