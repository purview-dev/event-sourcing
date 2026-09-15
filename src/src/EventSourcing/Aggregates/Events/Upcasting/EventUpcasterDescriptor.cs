namespace Purview.EventSourcing.Aggregates.Events.Upcasting;

/// <summary>
/// Bridges a closed-generic <see cref="IEventUpcaster{TSource,TTarget}"/> to the non-generic
/// <see cref="IEventUpcasterDescriptor"/> so that all upcasters can be stored and looked up
/// by source type at runtime.
/// </summary>
/// <typeparam name="TSource">The legacy event type.</typeparam>
/// <typeparam name="TTarget">The current event type.</typeparam>
public sealed class EventUpcasterDescriptor<TSource, TTarget> : IEventUpcasterDescriptor
	where TSource : class
	where TTarget : class
{
	readonly IEventUpcaster<TSource, TTarget> _upcaster;

	/// <summary>
	/// Initialises a new <see cref="EventUpcasterDescriptor{TSource,TTarget}"/> wrapping the
	/// given <paramref name="upcaster"/>.
	/// </summary>
	public EventUpcasterDescriptor(IEventUpcaster<TSource, TTarget> upcaster)
	{
		ArgumentNullException.ThrowIfNull(upcaster);
		_upcaster = upcaster;
	}

	/// <inheritdoc/>
	public Type SourceType => typeof(TSource);

	/// <inheritdoc/>
	public Type TargetType => typeof(TTarget);

	/// <inheritdoc/>
	public object Upcast(object source)
	{
		ArgumentNullException.ThrowIfNull(source);

		return source is TSource typed
			? _upcaster.Upcast(typed)
			: throw new InvalidOperationException(
				$"Cannot upcast event of type '{source.GetType().FullName}' using upcaster for '{typeof(TSource).FullName}'."
			);
	}
}
