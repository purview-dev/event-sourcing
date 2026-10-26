namespace Purview.EventSourcing.Admin.Models;

/// <summary>
/// Identifies the capabilities exposed by the admin portal.
/// </summary>
public enum AdminFeature
{
	/// <summary>
	/// Search for aggregates across the event store.
	/// </summary>
	SearchAggregates,

	/// <summary>
	/// View the details of a single aggregate.
	/// </summary>
	ViewAggregate,

	/// <summary>
	/// View the event history of an aggregate stream.
	/// </summary>
	ViewEvents,

	/// <summary>
	/// View the serialized payloads contained in an aggregate's event history.
	/// </summary>
	ViewEventPayloads,

	/// <summary>
	/// Project aggregate state at a specific point in time.
	/// </summary>
	ProjectPointInTime,

	/// <summary>
	/// Export events from an aggregate stream.
	/// </summary>
	ExportEvents,

	/// <summary>
	/// View the event-store capability contract and operational health summary.
	/// </summary>
	ViewCapabilities,

	/// <summary>
	/// View poisoned (dead-letter) transactional outbox messages.
	/// </summary>
	ViewPoisonedOutbox,

	/// <summary>
	/// View the runtime event-contract manifest and its compatibility status.
	/// </summary>
	ViewManifest,

	/// <summary>
	/// View stored event type names the runtime cannot resolve (unknown events that would be skipped on replay).
	/// </summary>
	ViewUnknownEvents,

	/// <summary>
	/// View the snapshot status of an aggregate stream.
	/// </summary>
	ViewSnapshot,

	/// <summary>
	/// Rebuild an aggregate snapshot from its canonical event stream.
	/// </summary>
	RebuildSnapshot,
}
