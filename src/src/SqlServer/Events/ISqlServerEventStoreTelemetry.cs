using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Purview.Telemetry;

namespace Purview.EventSourcing.SqlServer.Events;

/// <summary>
/// Telemetry contract for the SQL Server event store, covering distributed tracing, metrics, and logging.
/// </summary>
/// <remarks>
/// Implemented by the source generator to emit activities, counters, and log statements for
/// <see cref="SqlServerEventStore{T}"/> operations.
/// </remarks>
[ActivitySource]
[Logger]
[Meter]
public interface ISqlServerEventStoreTelemetry
{
	// Activities (distributed tracing)

	/// <summary>
	/// Starts an activity representing a get-aggregate operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being retrieved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <returns>The activity, or <see langword="null"/> when tracing is not enabled.</returns>
	[Activity]
	Activity? GetAggregate(string aggregateId, [Baggage] string aggregateTypeFullName);

	/// <summary>
	/// Starts an activity representing a get-aggregate-at-version operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being retrieved.</param>
	/// <param name="version">The version of the aggregate being retrieved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <returns>The activity, or <see langword="null"/> when tracing is not enabled.</returns>
	[Activity]
	Activity? GetAggregateAtVersion(string aggregateId, int version, [Baggage] string aggregateTypeFullName);

	/// <summary>
	/// Starts an activity representing a save-aggregate operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being saved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <returns>The activity, or <see langword="null"/> when tracing is not enabled.</returns>
	[Activity]
	Activity? SaveAggregate(string aggregateId, [Baggage] string aggregateTypeFullName);

	/// <summary>
	/// Starts an activity representing a delete-aggregate operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being deleted.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <returns>The activity, or <see langword="null"/> when tracing is not enabled.</returns>
	[Activity]
	Activity? DeleteAggregate(string aggregateId, [Baggage] string aggregateTypeFullName);

	/// <summary>
	/// Records the number of events reconstituted when loading an aggregate.
	/// </summary>
	/// <param name="activity">The activity to attach the event to.</param>
	/// <param name="eventCount">The number of events applied.</param>
	/// <param name="version">The resulting aggregate version.</param>
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
	/// Increments the counter tracking loaded aggregates.
	/// </summary>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[AutoCounter]
	void AggregateLoaded(string aggregateType);

	/// <summary>
	/// Increments the counter tracking saved aggregates.
	/// </summary>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[AutoCounter]
	void AggregateSaved(string aggregateType);

	/// <summary>
	/// Increments the counter tracking deleted aggregates.
	/// </summary>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[AutoCounter]
	void AggregateDeletedCounter(string aggregateType);

	// Logging

	/// <summary>
	/// Logs that an aggregate was retrieved from the distributed cache.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	[Log(LogLevel.Debug)]
	void AggregateRetrievedFromCache(string aggregateId, string aggregateTypeFullName);

	/// <summary>
	/// Logs the start of a get-aggregate operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate being retrieved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	[Log(LogLevel.Debug)]
	void GetAggregateStart(string aggregateId, string aggregateTypeFullName);

	/// <summary>
	/// Logs a failure to retrieve an aggregate at a specific version.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="specificVersion">The version that could not be retrieved.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Error)]
	void GetAggregateAtSpecificVersionFailed(
		string aggregateId,
		string aggregateTypeFullName,
		int specificVersion,
		Exception exception
	);

	/// <summary>
	/// Logs that an aggregate was reconstituted from its persisted events.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	/// <param name="eventCount">The number of events applied.</param>
	/// <param name="versionData">The version state after reconstitution.</param>
	[Log(LogLevel.Debug)]
	void ReconstitutedAggregateFromEvents(
		string aggregateId,
		string aggregateTypeFullName,
		string aggregateType,
		int eventCount,
		AggregateVersionData versionData
	);

	/// <summary>
	/// Logs the start of a get-aggregate-at-version operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="specificVersion">The version being retrieved.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	[Log(LogLevel.Debug)]
	void GetAggregateAtSpecificVersionStart(string aggregateId, int specificVersion, string aggregateTypeFullName);

	/// <summary>
	/// Logs that a save operation contained no changes.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[Log(LogLevel.Debug)]
	void SaveContainedNoChanges(string aggregateId, string aggregateTypeFullName, string aggregateType);

	/// <summary>
	/// Logs that an unknown event type was skipped during reconstitution.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	/// <param name="eventType">The name of the event that could not be mapped.</param>
	/// <param name="aggregateVersion">The version at which the unknown event occurred.</param>
	[Log(LogLevel.Warning)]
	void SkippedUnknownEvent(
		string aggregateId,
		string aggregateTypeFullName,
		string aggregateType,
		string eventType,
		int aggregateVersion
	);

	/// <summary>
	/// Logs that a persisted event could not be applied to the aggregate.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	/// <param name="eventType">The name of the event.</param>
	/// <param name="eventTypeFullName">The full name of the event type.</param>
	/// <param name="aggregateVersion">The version at which the event occurred.</param>
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
	/// Logs the completion of a get-aggregate operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="elapsedMilliseconds">The elapsed time of the operation in milliseconds.</param>
	[Log(LogLevel.Debug)]
	void GetAggregateComplete(string aggregateId, string aggregateTypeFullName, long elapsedMilliseconds);

	/// <summary>
	/// Logs a failure to deserialize an aggregate snapshot.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Error)]
	void SnapshotDeserializationFailed(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs a failure while writing a best-effort snapshot after the events were committed.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Error)]
	void SnapshotWriteFailure(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs a failure while reading an aggregate from the distributed cache.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Warning)]
	void CacheGetFailure(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs a failure while updating the distributed cache.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Warning)]
	void CacheUpdateFailure(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that an aggregate was deleted.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[Log(LogLevel.Debug)]
	void AggregateDeleted(string aggregateId, string aggregateTypeFullName, string aggregateType);

	/// <summary>
	/// Logs that an aggregate was restored.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[Log(LogLevel.Debug)]
	void AggregateRestored(string aggregateId, string aggregateTypeFullName, string aggregateType);

	/// <summary>
	/// Logs that a save operation was invoked.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[Log(LogLevel.Debug)]
	void SaveCalled(string aggregateId, string aggregateTypeFullName, string aggregateType);

	/// <summary>
	/// Logs a failure at the storage layer during a save operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Error)]
	void SaveFailedAtStorage(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that the idempotency check found the events already applied.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="idempotencyId">The idempotency identifier.</param>
	[Log(LogLevel.Debug)]
	void EventsAlreadyApplied(string aggregateId, string idempotencyId);

	/// <summary>
	/// Logs a failure during a save operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Error)]
	void SaveFailed(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that an event type could not be mapped.
	/// </summary>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="eventType">The name of the event type that could not be mapped.</param>
	[Log(LogLevel.Warning)]
	void MissingEventType(string aggregateTypeFullName, string eventType);

	/// <summary>
	/// Logs the completion of a get-aggregate-at-version operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="specificVersion">The version that was retrieved.</param>
	/// <param name="elapsedMilliseconds">The elapsed time of the operation in milliseconds.</param>
	[Log(LogLevel.Debug)]
	void GetAggregateAtSpecificVersionComplete(
		string aggregateId,
		string aggregateTypeFullName,
		int specificVersion,
		long elapsedMilliseconds
	);

	/// <summary>
	/// Logs a failure to remove an aggregate from the distributed cache.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Warning)]
	void CacheRemovalFailure(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs a failure to deserialize a persisted event.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Warning)]
	void EventDeserializationFailed(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that an aggregate was successfully saved.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="eventCount">The number of events saved.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	[Log(LogLevel.Debug)]
	void SavedAggregate(string aggregateId, string aggregateTypeFullName, int eventCount, string aggregateType);

	/// <summary>
	/// Logs a failure during a get-aggregate operation.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Error)]
	void GetAggregateFailed(string aggregateId, string aggregateTypeFullName, Exception exception);

	/// <summary>
	/// Logs that an expected stream-version row could not be found.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="aggregateTypeName">The short name of the aggregate type.</param>
	/// <param name="aggregateTypeFullName">The full name of the aggregate type.</param>
	[Log(LogLevel.Warning)]
	void StreamVersionExpectedToExistButNotFound(
		string aggregateId,
		string aggregateTypeName,
		string aggregateTypeFullName
	);

	/// <summary>
	/// Logs that a stream-version row was not found.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	[Log(LogLevel.Debug)]
	void StreamVersionNotFound(string aggregateId);

	/// <summary>
	/// Logs that a stream-version row was found.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="streamVersion">The persisted stream version.</param>
	/// <param name="aggregateType">The short name of the aggregate type.</param>
	/// <param name="isDeleted">Whether the aggregate is soft-deleted.</param>
	[Log(LogLevel.Debug)]
	void StreamVersionFound(string aggregateId, int streamVersion, string aggregateType, bool isDeleted);

	/// <summary>
	/// Logs a failure while retrieving the stream version.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Error)]
	void GetStreamVersionFailed(string aggregateId, Exception exception);

	/// <summary>
	/// Logs the start of a stream-version retrieval.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	[Log(LogLevel.Debug)]
	void GetStreamVersionStart(string aggregateId);

	/// <summary>
	/// Logs the completion of a stream-version retrieval.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="elapsedMilliseconds">The elapsed time of the operation in milliseconds.</param>
	[Log(LogLevel.Debug)]
	void GetStreamVersionComplete(string aggregateId, long elapsedMilliseconds);

	/// <summary>
	/// Logs that a permanent delete was requested.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	[Log(LogLevel.Debug)]
	void PermanentDeleteRequested(string aggregateId);

	/// <summary>
	/// Logs a failure during a permanent delete.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Critical)]
	void PermanentDeleteFailed(string aggregateId, Exception exception);

	/// <summary>
	/// Logs that a permanent delete completed.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	[Log(LogLevel.Debug)]
	void PermanentDeleteComplete(string aggregateId);

	/// <summary>
	/// Logs a failure while retrieving an idempotency marker.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="idempotencyId">The idempotency identifier.</param>
	/// <param name="exception">The exception that was thrown.</param>
	[Log(LogLevel.Error)]
	void GetIdempotencyMarkerFailed(string aggregateId, string idempotencyId, Exception exception);
}
