using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Services;

/// <summary>
/// Maps between aggregate event types and their persisted event names, and initializes the event-type
/// registration for an aggregate.
/// </summary>
/// <remarks>
/// Persisted event names are stable strings that survive type renames. The mapper derives a name from the
/// event type (optionally namespaced under the aggregate) and keeps the reverse lookup available for
/// replay so stored names can be resolved back to CLR types.
/// </remarks>
public interface IAggregateEventNameMapper
{
	/// <summary>
	/// Gets the persisted event name for the supplied event instance of the aggregate type.
	/// </summary>
	/// <typeparam name="T">The aggregate type.</typeparam>
	/// <param name="aggregateEvent">The event to name.</param>
	/// <returns>The persisted event name.</returns>
	string GetName<T>(object aggregateEvent)
		where T : IAggregate;

	/// <summary>
	/// Gets the persisted event name for the supplied event type of the aggregate type.
	/// </summary>
	/// <typeparam name="T">The aggregate type.</typeparam>
	/// <param name="aggregateEventType">The event type to name.</param>
	/// <returns>The persisted event name.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the aggregate type has not been registered via <see cref="InitializeAggregate{T}"/>.
	/// </exception>
	string GetName<T>(Type aggregateEventType)
		where T : IAggregate;

	/// <summary>
	/// Resolves a persisted event name back to the assembly-qualified name of the event type.
	/// </summary>
	/// <typeparam name="T">The aggregate type.</typeparam>
	/// <param name="eventTypeName">The persisted event name to resolve.</param>
	/// <returns>The assembly-qualified type name, or null when the name is not registered.</returns>
	string? GetTypeName<T>(string eventTypeName)
		where T : IAggregate;

	/// <summary>
	/// Resolves a persisted event name back to the assembly-qualified name of the event type,
	/// regardless of the aggregate. Returns null when the name is not registered.
	/// </summary>
	/// <param name="eventTypeName">The persisted event name to resolve.</param>
	/// <returns>The assembly-qualified type name, or null when the name is not registered.</returns>
	string? GetTypeName(string eventTypeName)
	{
		ArgumentNullException.ThrowIfNull(eventTypeName);
		return null;
	}

	/// <summary>
	/// Registers the aggregate's event types and returns its aggregate type name.
	/// </summary>
	/// <typeparam name="T">The aggregate type to initialize.</typeparam>
	/// <returns>The persisted aggregate type name.</returns>
	/// <remarks>Called before an aggregate is first persisted so its event names can be mapped during replay.</remarks>
	string InitializeAggregate<T>()
		where T : class, IAggregate, new();
}
