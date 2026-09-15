namespace Purview.EventSourcing.Aggregates.Exceptions;

/// <summary>
/// Indicates an <see cref="IEvent"/> was applied to an <see cref="IAggregate"/>,
/// but the event type was unregistered.
/// </summary>
public sealed class UnregisteredEventException : Exception
{
	/// <summary>
	/// Initializes a new instance of the <see cref="UnregisteredEventException"/> class.
	/// </summary>
	public UnregisteredEventException() { }

	/// <summary>
	/// Initializes a new instance of the <see cref="UnregisteredEventException"/> class with a specified message.
	/// </summary>
	/// <param name="message">The message that describes the error.</param>
	public UnregisteredEventException(string message)
		: base(message) { }

	/// <summary>
	/// Initializes a new instance of the <see cref="UnregisteredEventException"/> class with a specified message
	/// and a reference to the inner exception that is the cause of this exception.
	/// </summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="inner">The exception that is the cause of the current exception.</param>
	public UnregisteredEventException(string message, Exception inner)
		: base(message, inner) { }

	/// <summary>
	/// Initializes a new instance of the <see cref="UnregisteredEventException"/> class for an event type
	/// that was applied to an aggregate without being registered.
	/// </summary>
	/// <param name="eventType">The type of the event that was not registered.</param>
	/// <param name="aggregate">The aggregate the event was applied to.</param>
	public UnregisteredEventException(Type eventType, IAggregate aggregate)
		: base($"The event type '{eventType}' is not a registered event for aggregate type {aggregate}.")
	{
		EventType = eventType;
		Aggregate = aggregate;
	}

	/// <summary>
	/// The aggregate that received an unregistered event type.
	/// </summary>
	public IAggregate? Aggregate { get; }

	/// <summary>
	/// The type of the event.
	/// </summary>
	public Type? EventType { get; }
}
