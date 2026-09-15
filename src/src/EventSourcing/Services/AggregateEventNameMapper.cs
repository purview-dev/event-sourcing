using System.Collections.Concurrent;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events.Upcasting;

namespace Purview.EventSourcing.Services;

sealed partial class AggregateEventNameMapper(IEnumerable<IEventUpcasterDescriptor>? upcasters = null)
	: IAggregateEventNameMapper
{
	readonly ConcurrentDictionary<string, string> _eventNamesByAssemblyTypeName = new(StringComparer.InvariantCulture);
	readonly ConcurrentDictionary<string, string> _eventNamesByDefinedTypeName = new(StringComparer.InvariantCulture);
	readonly ConcurrentDictionary<string, string> _registeredAggregateTypes = new(StringComparer.InvariantCulture);

	// Forward cache keyed by CLR type that avoids building `Type.AssemblyQualifiedName` (a fresh
	// string on every call) on the hot save path.
	readonly ConcurrentDictionary<Type, string> _eventNamesByType = new();
	readonly Type[] _upcasterSourceTypes =
		upcasters?.Select(upcaster => upcaster.SourceType).Distinct().ToArray() ?? [];

	public string GetName<T>(object @event)
		where T : IAggregate => GetName<T>(@event.GetType());

	public string GetName<T>(Type aggregateEventType)
		where T : IAggregate
	{
		if (_eventNamesByType.TryGetValue(aggregateEventType, out var eventName))
			return eventName;

		var eventTypeAssemblyQualifiedName = aggregateEventType.AssemblyQualifiedName.OrDefault(
			aggregateEventType.ToString()
		);
		if (_eventNamesByAssemblyTypeName.TryGetValue(eventTypeAssemblyQualifiedName, out eventName))
		{
			_eventNamesByType.TryAdd(aggregateEventType, eventName);
			return eventName;
		}

		eventName = CreateEventName(aggregateEventType, typeof(T).FullName!);

		if (_eventNamesByAssemblyTypeName.TryAdd(eventTypeAssemblyQualifiedName, eventName))
			_eventNamesByDefinedTypeName.TryAdd(eventName, eventTypeAssemblyQualifiedName);

		_eventNamesByType.TryAdd(aggregateEventType, eventName);

		return eventName;
	}

	public string? GetTypeName<T>(string eventTypeName)
		where T : IAggregate
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(eventTypeName);

		return _eventNamesByDefinedTypeName.TryGetValue(eventTypeName, out var eventName) ? eventName : null;
	}

	public string? GetTypeName(string eventTypeName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(eventTypeName);

		return _eventNamesByDefinedTypeName.TryGetValue(eventTypeName, out var eventName) ? eventName : null;
	}

	public string InitializeAggregate<T>()
		where T : class, IAggregate, new()
	{
		var aggregateType = typeof(T);
		var aggregateTypeFullName = aggregateType.FullName!;

		return _registeredAggregateTypes.GetOrAdd(
			aggregateTypeFullName,
			_ =>
			{
				T aggregateInstance = new();
				var aggregateName = aggregateInstance.AggregateType;

				Populate<T>(aggregateName, [.. aggregateInstance.GetRegisteredEventTypes()]);

				return aggregateName;
			}
		);
	}

	/// <summary>
	/// Builds the persisted event name for <paramref name="aggregateEventType"/>, optionally
	/// namespaced under the aggregate registered for <paramref name="aggregateTypeFullName"/>.
	/// </summary>
	/// <param name="aggregateEventType">The event type to name.</param>
	/// <param name="aggregateTypeFullName">The full name of the owning aggregate type.</param>
	/// <returns>The persisted event name.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the event name would be namespaced but the aggregate type has not been
	/// registered via <see cref="InitializeAggregate{T}"/>.
	/// </exception>
	string CreateEventName(Type aggregateEventType, string aggregateTypeFullName)
	{
		var eventName = TypeNameHelper.GetName(aggregateEventType, "Event", true);
		if (eventName != aggregateEventType.FullName)
		{
			if (!_registeredAggregateTypes.TryGetValue(aggregateTypeFullName, out var aggregateName))
				throw new InvalidOperationException(
					$"{aggregateTypeFullName} has not been registered, call InitializeAggregate."
				);

			eventName = $"{aggregateName}.{eventName}";
		}

		return eventName;
	}

	void Populate<T>(string aggregateName, Type[] aggregateEvents)
		where T : IAggregate
	{
		if (aggregateEvents != null && aggregateEvents.Length > 0)
		{
			for (var i = 0; i < aggregateEvents.Length; i++)
			{
				var aggregateEventType = aggregateEvents[i];
				var eventTypeAssemblyQualifiedName = aggregateEventType.AssemblyQualifiedName.OrDefault(
					aggregateEventType.ToString()
				);
				if (!_eventNamesByAssemblyTypeName.TryGetValue(eventTypeAssemblyQualifiedName, out var _))
				{
					var eventName = TypeNameHelper.GetName(aggregateEventType, "Event", true);
					if (eventName != aggregateEventType.FullName)
						eventName = $"{aggregateName}.{eventName}";

					if (_eventNamesByAssemblyTypeName.TryAdd(eventTypeAssemblyQualifiedName, eventName))
					{
						_eventNamesByDefinedTypeName.TryAdd(eventName, eventTypeAssemblyQualifiedName);
						_eventNamesByType.TryAdd(aggregateEventType, eventName);
					}
				}
			}
		}

		// Legacy event types only exist as upcaster sources; register them so stored names
		// can be resolved back to CLR types during replay.
		if (_upcasterSourceTypes.Length == 0)
			return;

		for (var i = 0; i < _upcasterSourceTypes.Length; i++)
		{
			var sourceType = _upcasterSourceTypes[i];
			var eventTypeAssemblyQualifiedName = sourceType.AssemblyQualifiedName.OrDefault(sourceType.ToString());
			if (_eventNamesByAssemblyTypeName.ContainsKey(eventTypeAssemblyQualifiedName))
				continue;

			var eventName = TypeNameHelper.GetName(sourceType, "Event", true);
			if (eventName != sourceType.FullName)
				eventName = $"{aggregateName}.{eventName}";

			if (_eventNamesByAssemblyTypeName.TryAdd(eventTypeAssemblyQualifiedName, eventName))
			{
				_eventNamesByDefinedTypeName.TryAdd(eventName, eventTypeAssemblyQualifiedName);
				_eventNamesByType.TryAdd(sourceType, eventName);
			}
		}
	}
}
