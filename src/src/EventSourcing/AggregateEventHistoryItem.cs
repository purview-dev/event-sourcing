namespace Purview.EventSourcing;

/// <summary>
/// A single event-history entry for an aggregate.
/// </summary>
public sealed class AggregateEventHistoryItem
{
	/// <summary>
	/// Gets or sets the id of the aggregate the event belongs to.
	/// </summary>
	public string AggregateId { get; set; } = default!;

	/// <summary>
	/// Gets or sets the persisted aggregate type name.
	/// </summary>
	public string AggregateType { get; set; } = default!;

	/// <summary>
	/// Gets or sets the persisted event name.
	/// </summary>
	public string EventType { get; set; } = default!;

	/// <summary>
	/// Gets or sets the CLR type name of the event.
	/// </summary>
	public string EventClrType { get; set; } = default!;

	/// <summary>
	/// Gets or sets the persisted schema version of the event contract.
	/// </summary>
	public int SchemaVersion { get; set; } = 1;

	/// <summary>
	/// Gets or sets the aggregate version the event was recorded at.
	/// </summary>
	public long AggregateVersion { get; set; }

	/// <summary>
	/// Gets or sets the UTC timestamp the event was recorded at.
	/// </summary>
	public DateTimeOffset When { get; set; }

	/// <summary>
	/// Gets or sets the idempotency id used to detect duplicate saves, when one was supplied.
	/// </summary>
	public string? IdempotencyId { get; set; }

	/// <summary>
	/// Gets or sets the user id associated with the event, when one was supplied.
	/// </summary>
	public string? UserId { get; set; }

	/// <summary>
	/// Gets or sets the id of the event that caused this event, when one was supplied.
	/// </summary>
	public string? CausationId { get; set; }

	/// <summary>
	/// Gets or sets the correlation id that groups this event with related events, when one was supplied.
	/// </summary>
	public string? CorrelationId { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the event could not be mapped to a registered CLR type.
	/// </summary>
	public bool IsUnknownEvent { get; set; }

	/// <summary>
	/// Gets or sets the serialized event payload.
	/// </summary>
	public string? Payload { get; set; }
}
