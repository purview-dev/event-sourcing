using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// One event delivered to an <see cref="IEventSubscriber{T}"/>.
/// </summary>
/// <typeparam name="T">The aggregate type the subscription reads.</typeparam>
/// <param name="AggregateId">The aggregate the event belongs to.</param>
/// <param name="Record">The event payload and its framework-managed metadata.</param>
/// <param name="EventType">The persisted event type name.</param>
/// <param name="Scope">The tenant the subscription is running as.</param>
public sealed record EventSubscriptionContext<T>(
	string AggregateId,
	EventRecord Record,
	string EventType,
	TenantScope Scope
)
	where T : class, IAggregate, new()
{
	/// <summary>The event payload.</summary>
	public object Event => Record.Event;

	/// <summary>The framework-managed metadata for the event.</summary>
	public EventMetadata Metadata => Record.Metadata;

	/// <summary>The aggregate version the event was recorded at.</summary>
	public long AggregateVersion => Record.Metadata.AggregateVersion;

	/// <summary>
	/// <see langword="true"/> when the persisted event type could not be resolved to a CLR type.
	/// </summary>
	/// <remarks>
	/// A subscriber that does not understand the event should return without doing anything. The
	/// subscription still advances past it, because an event the application has deleted the contract
	/// for must not stall every later event in the stream.
	/// </remarks>
	public bool IsUnresolved => Record.Event is UnknownEvent;
}
