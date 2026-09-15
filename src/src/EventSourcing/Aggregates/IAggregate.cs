using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.Aggregates;

/// <summary>
/// A chronological series of events, modifying state are they applied,
/// in-order to represent a final state up until the last event applied.
/// </summary>
public interface IAggregate
{
	/// <summary>
	/// Gets the <see cref="AggregateDetails"/> representing
	/// information about the <see cref="IAggregate"/>.
	/// </summary>
	/// <remarks>Do not manually edit this instance or any of it's properties.</remarks>
	[NotNull]
	AggregateDetails Details { get; init; }

	/// <summary>
	/// This should represent a unique type name within your persistence layer.
	/// </summary>
	/// <remarks>Some implementations of the <see cref="IEventStore{T}"/> use this
	/// value a querying parameter.</remarks>
	[NotNull]
	string AggregateType { get; }

	/// <summary>
	/// Gets all of the currently unsaved events applied to this <see cref="IAggregate"/>,
	/// paired with their framework-managed <see cref="EventMetadata"/>.
	/// </summary>
	/// <returns>An array of the unsaved event records.</returns>
	IReadOnlyList<EventRecord> GetUnsavedEvents();

	/// <summary>
	/// Gets an array of the event types that this <see cref="IAggregate"/> can apply.
	/// </summary>
	/// <returns>An array of event types.</returns>
	IEnumerable<Type> GetRegisteredEventTypes();

	/// <summary>
	/// Gets a value indicating if there are any unsaved events
	/// on this <see cref="IAggregate"/> instance.
	/// </summary>
	/// <returns>True if there are unsaved events, otherwise, false.</returns>
	bool HasUnsavedEvents();

	/// <summary>
	/// Clears any unsaved events passed the
	/// specified <paramref name="upToVersion"/> and resets the
	/// <see cref="AggregateDetails.CurrentVersion"/> to the most recent value.
	/// </summary>
	/// <param name="upToVersion">Optional. The version (exclusive) of unsaved events to clear. A null value indicates all unsaved events will be cleared.</param>
	void ClearUnsavedEvents(int? upToVersion = null);

	/// <summary>
	/// <para>
	/// Applies an event instance, either from a live update, or from a persisted store. Only registered
	/// event types should be used.
	/// </para>
	/// <para>
	/// Any aggregate-related properties on the event instance should be reflected on the this instance.
	/// </para>
	/// </summary>
	/// <param name="aggregateEvent">The event instance to apply.</param>
	/// <param name="metadata">The framework-managed metadata for the event.</param>
	void ApplyEvent([DisallowNull] object aggregateEvent, EventMetadata metadata);

	/// <summary>
	/// Gets a value indicate if the event type can be applied to this instance.
	/// </summary>
	/// <param name="aggregateEvent">The event being tested.</param>
	/// <returns>True if this instance can process this event, otherwise, false.</returns>
	bool CanApplyEvent([DisallowNull] object aggregateEvent);
}
