using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.Aggregates;

/// <summary>
/// An unsaved event payload together with its framework-managed <see cref="EventMetadata"/>.
/// </summary>
public readonly record struct EventRecord(object Event, EventMetadata Metadata);
