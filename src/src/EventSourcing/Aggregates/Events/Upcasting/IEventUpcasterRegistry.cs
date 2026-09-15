namespace Purview.EventSourcing.Aggregates.Events.Upcasting;

/// <summary>
/// Maintains a registry of all registered <see cref="IEventUpcaster{TSource,TTarget}"/> instances
/// and applies them — potentially as a chain — to an event read from the store.
/// </summary>
/// <remarks>
/// <para>
/// The registry is built at startup from all <see cref="IEventUpcasterDescriptor"/> services
/// registered in the DI container. Call
/// <c>services.AddEventUpcaster&lt;TSource, TTarget, TUpcaster&gt;()</c> (defined on
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>) to register
/// individual upcasters.
/// </para>
/// <para>
/// When <see cref="Upcast"/> is called with an event whose runtime type has a registered
/// upcaster, the result is fed back into the registry until no further upcaster is found,
/// enabling multi-hop chains such as v1 → v2 → v3.
/// </para>
/// </remarks>
public interface IEventUpcasterRegistry
{
	/// <summary>
	/// Returns <see langword="true"/> when at least one upcaster is registered whose source type equals
	/// <paramref name="aggregateEvent"/>'s runtime type.
	/// </summary>
	/// <param name="aggregateEvent">The event to test.</param>
	bool CanUpcast(object aggregateEvent);

	/// <summary>
	/// Applies registered upcasters to <paramref name="aggregateEvent"/> in a chain until no further
	/// upcaster is found for the current runtime type.
	/// </summary>
	/// <param name="aggregateEvent">The event (potentially legacy) read from the event store.</param>
	/// <returns>
	/// The final up-cast event. If no upcaster is registered for the runtime type of
	/// <paramref name="aggregateEvent"/>, the original instance is returned unchanged.
	/// </returns>
	object Upcast(object aggregateEvent);
}
