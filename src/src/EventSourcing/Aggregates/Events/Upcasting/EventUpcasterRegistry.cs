namespace Purview.EventSourcing.Aggregates.Events.Upcasting;

/// <summary>
/// Default <see cref="IEventUpcasterRegistry"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// Built from all <see cref="IEventUpcasterDescriptor"/> services registered in the DI container.
/// Descriptors are created automatically when you call
/// <c>services.AddEventUpcaster&lt;TSource, TTarget, TUpcaster&gt;()</c>.
/// </para>
/// <para>
/// <see cref="Upcast"/> follows the upcasting chain until no further upcaster is found for the
/// current event type. This means multi-hop migrations (v1 → v2 → v3) are handled transparently
/// as long as each hop is registered.
/// </para>
/// </remarks>
public sealed class EventUpcasterRegistry : IEventUpcasterRegistry
{
	readonly Dictionary<Type, IEventUpcasterDescriptor> _upcastersBySourceType;

	/// <summary>
	/// Initialises the registry.
	/// </summary>
	/// <param name="descriptors">All registered upcaster descriptors.</param>
	/// <exception cref="InvalidOperationException">
	/// Thrown when a circular upcaster chain (for example v1 → v2 → v1) is detected. Same-type
	/// (in-place) upcasters are permitted and applied exactly once.
	/// </exception>
	public EventUpcasterRegistry(IEnumerable<IEventUpcasterDescriptor> descriptors)
	{
		ArgumentNullException.ThrowIfNull(descriptors);

		// Last-registered wins when the same source type appears more than once.
		_upcastersBySourceType = [];
		foreach (var descriptor in descriptors)
			_upcastersBySourceType[descriptor.SourceType] = descriptor;

		ValidateUpcastChains();
	}

	void ValidateUpcastChains()
	{
		foreach (var sourceType in _upcastersBySourceType.Keys)
		{
			HashSet<Type> visitedTypes = [];
			var current = sourceType;

			while (_upcastersBySourceType.TryGetValue(current, out var descriptor))
			{
				if (!visitedTypes.Add(current))
					throw new InvalidOperationException(
						$"Detected a circular upcaster chain involving event type '{current.FullName}'. "
							+ $"Upcasters must form a forward-only (v1 → v2 → v3) chain."
					);

				var next = descriptor.TargetType;
				if (next == current)
					break; // Same-type (in-place) upcaster: applied once and stops.

				current = next;
			}
		}
	}

	/// <inheritdoc/>
	public bool CanUpcast(object aggregateEvent)
	{
		ArgumentNullException.ThrowIfNull(aggregateEvent);
		return _upcastersBySourceType.ContainsKey(aggregateEvent.GetType());
	}

	/// <inheritdoc/>
	public object Upcast(object aggregateEvent)
	{
		ArgumentNullException.ThrowIfNull(aggregateEvent);

		var current = aggregateEvent;
		HashSet<Type> visitedTypes = [current.GetType()];

		// Follow the chain: v1 → v2 → v3 …
		while (_upcastersBySourceType.TryGetValue(current.GetType(), out var descriptor))
		{
			var next = descriptor.Upcast(current);
			var nextType = next.GetType();

			// A same-type (in-place) upcaster transforms the event in place and must only
			// be applied once, otherwise it would look like a degenerate cycle.
			if (nextType == current.GetType())
				return next;

			if (!visitedTypes.Add(nextType))
				throw new InvalidOperationException(
					$"Detected a cycle or degenerate upcast chain involving event type '{nextType.FullName}'."
				);

			current = next;
		}

		return current;
	}
}
