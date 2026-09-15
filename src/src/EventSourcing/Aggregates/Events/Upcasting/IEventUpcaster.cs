namespace Purview.EventSourcing.Aggregates.Events.Upcasting;

/// <summary>
/// Converts a persisted <typeparamref name="TSource"/> event (an older schema version) into a
/// <typeparamref name="TTarget"/> event (the current schema version).
/// </summary>
/// <typeparam name="TSource">The legacy event type being read from the store.</typeparam>
/// <typeparam name="TTarget">The current event type that the aggregate understands.</typeparam>
/// <remarks>
/// <para>
/// Register one or more upcasters with the DI container and they will be discovered automatically
/// by <see cref="IEventUpcasterRegistry"/>. When reading events from the store, the registry
/// applies all matching upcasters in a chain (e.g. v1 → v2 → v3) before passing the final
/// event to <see cref="IAggregate.ApplyEvent"/>.
/// </para>
/// <para>
/// Only forward-direction upcasters (older → newer) are supported. Upcasters transform event
/// payloads only; <see cref="EventMetadata"/> is carried by the framework and re-attached by the
/// store, so it should not be copied by the upcaster.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Suppose CustomerRegisteredEvent gains a required PhoneNumber property in v2.
/// public sealed class CustomerRegisteredV1ToV2Upcaster
///     : IEventUpcaster&lt;CustomerRegisteredEventV1, CustomerRegisteredEvent&gt;
/// {
///     public CustomerRegisteredEvent Upcast(CustomerRegisteredEventV1 source)
///         => new()
///         {
///             CustomerId = source.CustomerId,
///             Email      = source.Email,
///             PhoneNumber = string.Empty  // default for legacy events
///         };
/// }
/// </code>
/// </example>
public interface IEventUpcaster<TSource, TTarget>
	where TSource : class
	where TTarget : class
{
	/// <summary>
	/// Converts <paramref name="source"/> into an equivalent <typeparamref name="TTarget"/> instance.
	/// </summary>
	/// <param name="source">The legacy event read from the event store.</param>
	/// <returns>The up-cast event that the aggregate can apply.</returns>
	TTarget Upcast(TSource source);
}
