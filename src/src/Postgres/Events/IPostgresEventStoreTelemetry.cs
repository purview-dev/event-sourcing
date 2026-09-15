using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Purview.Telemetry;

namespace Purview.EventSourcing.Postgres.Events;

/// <summary>
/// Telemetry contract (activities, counters, and log messages) for the PostgreSQL event store.
/// </summary>
[ActivitySource]
[Logger]
[Meter]
public interface IPostgresEventStoreTelemetry
{
	// Activities (distributed tracing)

	/// <summary>
	/// Starts an activity for an aggregate get operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being retrieved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type being retrieved.</param>
	/// <returns>The started activity, or <see langword="null"/> when the activity source is disabled.</returns>
	[Activity]
	Activity? GetAggregate(string aggregateId, [Baggage] string aggregateTypeFullName);

	/// <summary>
	/// Starts an activity for an aggregate get-at-version operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being retrieved.</param>
	/// <param name="version">The version of the aggregate being retrieved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type being retrieved.</param>
	/// <returns>The started activity, or <see langword="null"/> when the activity source is disabled.</returns>
	[Activity]
	Activity? GetAggregateAtVersion(string aggregateId, int version, [Baggage] string aggregateTypeFullName);

	/// <summary>
	/// Starts an activity for an aggregate save operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being saved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type being saved.</param>
	/// <returns>The started activity, or <see langword="null"/> when the activity source is disabled.</returns>
	[Activity]
	Activity? SaveAggregate(string aggregateId, [Baggage] string aggregateTypeFullName);

	/// <summary>
	/// Starts an activity for an aggregate delete operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being deleted.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type being deleted.</param>
	/// <returns>The started activity, or <see langword="null"/> when the activity source is disabled.</returns>
	[Activity]
	Activity? DeleteAggregate(string aggregateId, [Baggage] string aggregateTypeFullName);

	/// <summary>
	/// Records the reconstitution of an aggregate from its events.
	/// </summary>
	/// <param name="activity">The activity to attach the event to.</param>
	/// <param name="eventCount">The number of events applied.</param>
	/// <param name="version">The aggregate version after reconstitution.</param>
	[Event]
	void EventsReconstituted(Activity? activity, int eventCount, int version);

	/// <summary>
	/// Records the completion of a save operation.
	/// </summary>
	/// <param name="activity">The activity to attach the event to.</param>
	/// <param name="eventCount">The number of events saved.</param>
	[Event]
	void SaveCompleted(Activity? activity, int eventCount);

	// Metrics (counters)

	/// <summary>
	/// Increments the counter recording loaded aggregates.
	/// </summary>
	/// <param name="aggregateType">The type of the aggregate that was loaded.</param>
	[AutoCounter]
	void AggregateLoaded(string aggregateType);

	/// <summary>
	/// Increments the counter recording saved aggregates.
	/// </summary>
	/// <param name="aggregateType">The type of the aggregate that was saved.</param>
	[AutoCounter]
	void AggregateSaved(string aggregateType);

	/// <summary>
	/// Increments the counter recording deleted aggregates.
	/// </summary>
	/// <param name="aggregateType">The type of the aggregate that was deleted.</param>
	[AutoCounter]
	void AggregateDeletedCounter(string aggregateType);

	// Logging

	/// <summary>
	/// Logs that an aggregate was retrieved from cache.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate retrieved from cache.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type retrieved from cache.</param>
	[Log(LogLevel.Debug)]
	void AggregateRetrievedFromCache(string aggregateId, string aggregateTypeFullName);

	/// <summary>
	/// Logs the start of an aggregate get operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being retrieved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type being retrieved.</param>
	[Log(LogLevel.Debug)]
	void GetAggregateStart(string aggregateId, string aggregateTypeFullName);

	/// <summary>
	/// Logs a failed aggregate get-at-version operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being retrieved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type being retrieved.</param>
	/// <param name="specificVersion">The version of the aggregate being retrieved.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Error)]
	void GetAggregateAtSpecificVersionFailed(
		string aggregateId,
		string aggregateTypeFullName,
		int specificVersion,
		Exception exception
	);

	/// <summary>
	/// Logs the reconstitution of an aggregate from its events.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that was reconstituted.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that was reconstituted.</param>
	/// <param name="aggregateType">The short type name of the aggregate that was reconstituted.</param>
	/// <param name="eventCount">The number of events applied.</param>
	/// <param name="versionData">The version data of the aggregate after reconstitution.</param>
	[Log(LogLevel.Debug)]
	void ReconstitutedAggregateFromEvents(
		string aggregateId,
		string aggregateTypeFullName,
		string aggregateType,
		int eventCount,
		AggregateVersionData versionData
	);

	/// <summary>
	/// Logs the start of an aggregate get-at-version operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being retrieved.</param>
	/// <param name="specificVersion">The version of the aggregate being retrieved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type being retrieved.</param>
	[Log(LogLevel.Debug)]
	void GetAggregateAtSpecificVersionStart(string aggregateId, int specificVersion, string aggregateTypeFullName);

	/// <summary>
	/// Logs that a save operation contained no changes.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that contained no changes.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that contained no changes.</param>
	/// <param name="aggregateType">The short type name of the aggregate that contained no changes.</param>
	[Log(LogLevel.Debug)]
	void SaveContainedNoChanges(string aggregateId, string aggregateTypeFullName, string aggregateType);

	/// <summary>
	/// Logs that an unknown event was skipped during reconstitution.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being reconstituted.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type being reconstituted.</param>
	/// <param name="aggregateType">The short type name of the aggregate being reconstituted.</param>
	/// <param name="eventType">The persisted name of the unknown event.</param>
	/// <param name="aggregateVersion">The version of the unknown event.</param>
	[Log(LogLevel.Warning)]
	void SkippedUnknownEvent(
		string aggregateId,
		string aggregateTypeFullName,
		string aggregateType,
		string eventType,
		int aggregateVersion
	);

	/// <summary>
	/// Logs that an event could not be applied during reconstitution.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being reconstituted.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type being reconstituted.</param>
	/// <param name="aggregateType">The short type name of the aggregate being reconstituted.</param>
	/// <param name="eventType">The persisted name of the event.</param>
	/// <param name="eventTypeFullName">The full name of the event type that could not be applied.</param>
	/// <param name="aggregateVersion">The version of the event that could not be applied.</param>
	[Log(LogLevel.Warning)]
	void CannotApplyEvent(
		string aggregateId,
		string aggregateTypeFullName,
		string aggregateType,
		string eventType,
		string eventTypeFullName,
		int aggregateVersion
	);

	/// <summary>
	/// Logs the completion of an aggregate get operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that was retrieved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that was retrieved.</param>
	/// <param name="elapsedMilliseconds">The duration of the operation in milliseconds.</param>
	[Log(LogLevel.Debug)]
	void GetAggregateComplete(string aggregateId, string aggregateTypeFullName, long elapsedMilliseconds);

	/// <summary>
	/// Logs that snapshot deserialization failed.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate whose snapshot failed to deserialize.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type whose snapshot failed to deserialize.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Error)]
	void SnapshotDeserializationFailed(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs a failure while writing a best-effort snapshot after the events were committed.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate whose snapshot failed to be written.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type whose snapshot failed to be written.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Error)]
	void SnapshotWriteFailure(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs a failed cache get operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that failed to be retrieved from cache.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that failed to be retrieved from cache.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Warning)]
	void CacheGetFailure(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs a failed cache update operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that failed to be updated in cache.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that failed to be updated in cache.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Warning)]
	void CacheUpdateFailure(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that an aggregate was deleted.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that was deleted.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that was deleted.</param>
	/// <param name="aggregateType">The short type name of the aggregate that was deleted.</param>
	[Log(LogLevel.Debug)]
	void AggregateDeleted(string aggregateId, string aggregateTypeFullName, string aggregateType);

	/// <summary>
	/// Logs that an aggregate was restored.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that was restored.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that was restored.</param>
	/// <param name="aggregateType">The short type name of the aggregate that was restored.</param>
	[Log(LogLevel.Debug)]
	void AggregateRestored(string aggregateId, string aggregateTypeFullName, string aggregateType);

	/// <summary>
	/// Logs that a save operation was invoked.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being saved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type being saved.</param>
	/// <param name="aggregateType">The short type name of the aggregate being saved.</param>
	[Log(LogLevel.Debug)]
	void SaveCalled(string aggregateId, string aggregateTypeFullName, string aggregateType);

	/// <summary>
	/// Logs that a save failed at the storage layer.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that failed to be saved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that failed to be saved.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Error)]
	void SaveFailedAtStorage(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that events were already applied for the idempotency id.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate whose events were already applied.</param>
	/// <param name="idempotencyId">The idempotency id that was already applied.</param>
	[Log(LogLevel.Debug)]
	void EventsAlreadyApplied(string aggregateId, string idempotencyId);

	/// <summary>
	/// Logs that a save operation failed.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that failed to be saved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that failed to be saved.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Error)]
	void SaveFailed(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that the type for a persisted event name could not be resolved.
	/// </summary>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type being reconstituted.</param>
	/// <param name="eventType">The persisted event name that could not be resolved.</param>
	[Log(LogLevel.Warning)]
	void MissingEventType(string aggregateTypeFullName, string eventType);

	/// <summary>
	/// Logs the completion of an aggregate get-at-version operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that was retrieved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that was retrieved.</param>
	/// <param name="specificVersion">The version of the aggregate that was retrieved.</param>
	/// <param name="elapsedMilliseconds">The duration of the operation in milliseconds.</param>
	[Log(LogLevel.Debug)]
	void GetAggregateAtSpecificVersionComplete(
		string aggregateId,
		string aggregateTypeFullName,
		int specificVersion,
		long elapsedMilliseconds
	);

	/// <summary>
	/// Logs a failed cache removal operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that failed to be removed from cache.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that failed to be removed from cache.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Warning)]
	void CacheRemovalFailure(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs a failed event deserialization operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being reconstituted.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type being reconstituted.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Warning)]
	void EventDeserializationFailed(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that an aggregate was saved.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that was saved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that was saved.</param>
	/// <param name="eventCount">The number of events saved.</param>
	/// <param name="aggregateType">The short type name of the aggregate that was saved.</param>
	[Log(LogLevel.Debug)]
	void SavedAggregate(string aggregateId, string aggregateTypeFullName, int eventCount, string aggregateType);

	/// <summary>
	/// Logs that an aggregate get operation failed.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that failed to be retrieved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type that failed to be retrieved.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Error)]
	void GetAggregateFailed(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that an expected stream version was not found.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate whose stream version was expected but not found.</param>
	/// <param name="aggregateTypeName">The short type name of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	[Log(LogLevel.Warning)]
	void StreamVersionExpectedToExistButNotFound(
		string aggregateId,
		string aggregateTypeName,
		string aggregateTypeFullName
	);

	/// <summary>
	/// Logs that a stream version was not found.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate whose stream version was not found.</param>
	[Log(LogLevel.Debug)]
	void StreamVersionNotFound(string aggregateId);

	/// <summary>
	/// Logs that a stream version was found.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate whose stream version was found.</param>
	/// <param name="streamVersion">The stream version that was found.</param>
	/// <param name="aggregateType">The short type name of the aggregate.</param>
	/// <param name="isDeleted">Whether the aggregate is deleted.</param>
	[Log(LogLevel.Debug)]
	void StreamVersionFound(string aggregateId, int streamVersion, string aggregateType, bool isDeleted);

	/// <summary>
	/// Logs that retrieving a stream version failed.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate whose stream version failed to be retrieved.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Error)]
	void GetStreamVersionFailed(string aggregateId, Exception exception);

	/// <summary>
	/// Logs the start of a stream version retrieval.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate whose stream version is being retrieved.</param>
	[Log(LogLevel.Debug)]
	void GetStreamVersionStart(string aggregateId);

	/// <summary>
	/// Logs the completion of a stream version retrieval.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate whose stream version was retrieved.</param>
	/// <param name="elapsedMilliseconds">The duration of the operation in milliseconds.</param>
	[Log(LogLevel.Debug)]
	void GetStreamVersionComplete(string aggregateId, long elapsedMilliseconds);

	/// <summary>
	/// Logs that a permanent delete was requested.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being permanently deleted.</param>
	[Log(LogLevel.Debug)]
	void PermanentDeleteRequested(string aggregateId);

	/// <summary>
	/// Logs that a permanent delete failed.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that failed to be permanently deleted.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Critical)]
	void PermanentDeleteFailed(string aggregateId, Exception exception);

	/// <summary>
	/// Logs the completion of a permanent delete operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate that was permanently deleted.</param>
	[Log(LogLevel.Debug)]
	void PermanentDeleteComplete(string aggregateId);

	/// <summary>
	/// Logs that retrieving an idempotency marker failed.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate whose idempotency marker failed to be retrieved.</param>
	/// <param name="idempotencyId">The idempotency id that failed to be retrieved.</param>
	/// <param name="exception">The exception that caused the failure.</param>
	[Log(LogLevel.Error)]
	void GetIdempotencyMarkerFailed(string aggregateId, string idempotencyId, Exception exception);
}
