using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Aggregates.Exceptions;

namespace Purview.EventSourcing.Aggregates;

/// <summary>
/// A default base class for an <see cref="IAggregate"/> implementation.
/// </summary>
public abstract class AggregateBase : IAggregate
{
	/// <summary>
	/// Per-aggregate-type applier maps built once on first construction (G1). Appliers are open
	/// delegates that take the aggregate instance, so they are shared across all instances of a type.
	/// </summary>
	static readonly ConcurrentDictionary<Type, Dictionary<Type, Action<IAggregate, object>>> StaticApplierMaps = new();

	/// <summary>
	/// Types whose <see cref="RegisterEvents"/> uses instance-bound custom appliers
	/// (<see cref="Register{TEvent}(Action{TEvent})"/>), which cannot be shared statically.
	/// </summary>
	static readonly ConcurrentDictionary<Type, bool> DynamicTypes = new();

	static readonly Lock ApplierBuildLock = new();

	[ThreadStatic]
	static Dictionary<Type, Action<IAggregate, object>>? s_applierBuildTarget;

	[ThreadStatic]
	static bool s_dynamicDetected;

	/// <summary>
	/// The shared static applier map for this aggregate type, or <see langword="null"/> when the type
	/// uses per-instance registration.
	/// </summary>
	readonly Dictionary<Type, Action<IAggregate, object>>? _applierMap;

	Dictionary<Type, Action<IAggregate, object>>? _customAppliers;

	List<EventRecord> _unsavedEvents = [];

	EventRecord[]? _unsavedEventsSnapshot;

	List<SkippedEventRecord>? _skippedEvents;

	/// <summary>
	/// Gets the events that were skipped during replay because they could not be resolved or applied.
	/// </summary>
	public IReadOnlyList<SkippedEventRecord> SkippedEvents => _skippedEvents ?? [];

	/// <summary>
	/// Initializes the base-class.
	/// </summary>
	/// <param name="aggregateType">Specifies the <see cref="AggregateType"/>, if no value is specified
	/// <see cref="TypeNameHelper.GetName(Type, string, bool)"/> is called, with 'Aggregate' being trimmed if it's a suffix
	/// of the type name.</param>
	protected AggregateBase(string? aggregateType = null)
	{
		AggregateType = aggregateType ?? TypeNameHelper.GetName(GetType(), "Aggregate");

		var type = GetType();
		if (!StaticApplierMaps.TryGetValue(type, out _applierMap) && !TryBuildStaticAppliers(type, out _applierMap))
		{
			// Dynamic type: run registration per instance into a lazily-allocated dictionary.
			_customAppliers = [];
			RegisterSystemAppliers(_customAppliers);
#pragma warning disable CA2214 // Do not call overridable methods in constructors
			RegisterEvents();
#pragma warning restore CA2214 // Do not call overridable methods in constructors
		}

		_unsavedEvents = [];
	}

	///<inheritdoc/>
	public string AggregateType { get; }

	///<inheritdoc/>
	public AggregateDetails Details { get; init; } = new();

	static readonly Action<IAggregate, object> ApplyDeleted = static (aggregate, _) =>
		aggregate.Details.IsDeleted = true;

	static readonly Action<IAggregate, object> ApplyRestored = static (aggregate, _) =>
		aggregate.Details.IsDeleted = false;

	static readonly Action<IAggregate, object> ApplyForceSaved = static (_, _) => { };

	static void RegisterSystemAppliers(Dictionary<Type, Action<IAggregate, object>> target)
	{
		target[typeof(Deleted)] = ApplyDeleted;
		target[typeof(Restored)] = ApplyRestored;
		target[typeof(ForceSaved)] = ApplyForceSaved;
	}

	/// <summary>
	/// Used to register custom <see cref="EventContractAttribute"/> event implementations using the
	/// <see cref="Register{TEvent}(Action{TEvent})"/> method.
	/// </summary>
	protected abstract void RegisterEvents();

	///<inheritdoc/>
	public virtual void ClearUnsavedEvents(int? upToVersion = null)
	{
		var unsavedEventCount = _unsavedEvents.Count;
		if (upToVersion.HasValue)
			_unsavedEvents = [.. _unsavedEvents.Where(record => record.Metadata.AggregateVersion > upToVersion)];
		else
			_unsavedEvents.Clear();

		_unsavedEventsSnapshot = null;
		unsavedEventCount -= _unsavedEvents.Count;

		Details.CurrentVersion -= unsavedEventCount;
	}

	///<inheritdoc/>
	public IReadOnlyList<EventRecord> GetUnsavedEvents() => _unsavedEventsSnapshot ??= [.. _unsavedEvents];

	///<inheritdoc/>
	public bool HasUnsavedEvents() => _unsavedEvents.Count > 0;

	///<inheritdoc/>
	public bool CanApplyEvent(object aggregateEvent)
	{
		ArgumentNullException.ThrowIfNull(aggregateEvent);
		return (_applierMap is not null && _applierMap.ContainsKey(aggregateEvent.GetType()))
			|| (_customAppliers is not null && _customAppliers.ContainsKey(aggregateEvent.GetType()));
	}

	/// <summary>
	/// Records an event and stores the record ready for saving via
	/// <see cref="IEventStore{T}.SaveAsync(T, EventStoreOperationContext?, CancellationToken)"/>.
	/// The event is also applied once it's been recorded.
	/// </summary>
	/// <typeparam name="TEvent">The event implementation type.</typeparam>
	/// <param name="event">The event to save.</param>
	/// <returns>The current <see cref="AggregateBase"/> instance.</returns>
	protected internal AggregateBase RecordAndApply<TEvent>(TEvent @event)
	{
		ArgumentNullException.ThrowIfNull(@event, nameof(@event));

		var eventType = @event.GetType();
		if (!CanApplyEvent(@event))
			throw new UnregisteredEventException(eventType, this);

		if (Details.Locked)
			throw new LockedException(Details.Id);

		EventMetadata metadata = new(
			Details.CurrentVersion + 1,
			DateTimeOffset.UtcNow,
			GetSchemaVersion(eventType),
			IdempotencyId: null,
			CorrelationId: null,
			CausationId: null,
			UserId: null
		);

		_unsavedEvents.Add(new EventRecord(@event, metadata));
		_unsavedEventsSnapshot = null;

		ApplyCore(@event, metadata);

		return this;
	}

	/// <summary>
	/// Applies the <see cref="ForceSaved"/> to the aggregate, allowing the aggregate to be saved,
	/// regardless of other operations.
	/// </summary>
	/// <remarks>This is only applied if <see cref="HasUnsavedEvents"/> returns false.</remarks>
	public void ForceSave()
	{
		if (!HasUnsavedEvents())
			RecordAndApply(new ForceSaved());
	}

	internal void RecordSkippedEvent(int aggregateVersion, string? eventTypeName, bool isUnknown)
	{
		_skippedEvents ??= [];
		_skippedEvents.Add(new SkippedEventRecord(aggregateVersion, eventTypeName, isUnknown));
	}

	/// <summary>
	/// Registers an <see cref="Action{T}"/> as the handler to apply an event. Because the supplied
	/// delegate is instance-bound it cannot be shared statically; aggregates that use this method fall
	/// back to per-instance registration.
	/// </summary>
	protected void Register<TEvent>(Action<TEvent> applier)
		where TEvent : class
	{
		ArgumentNullException.ThrowIfNull(applier);

		if (s_applierBuildTarget is not null)
		{
			s_dynamicDetected = true;
			return;
		}

		_customAppliers ??= [];
		_customAppliers[typeof(TEvent)] = (aggregate, @event) => applier((TEvent)@event);
	}

	/// <summary>
	/// Registers a generated event applier (the generated <c>Apply(TEvent)</c> method) without
	/// allocating a per-instance delegate. Resolved once per aggregate/event type and cached.
	/// </summary>
	protected void RegisterGenerated<TEvent>()
		where TEvent : class
	{
		var applier = GetGeneratedApplier(GetType(), typeof(TEvent));

		if (s_applierBuildTarget is not null)
		{
			s_applierBuildTarget[typeof(TEvent)] = applier;
			return;
		}

		_customAppliers ??= [];
		_customAppliers[typeof(TEvent)] = applier;
	}

#pragma warning disable CA1033 // Interface methods should be callable by child types

	///<inheritdoc/>
	IEnumerable<Type> IAggregate.GetRegisteredEventTypes()
	{
		if (_applierMap is not null)
			return _applierMap.Keys;

		if (_customAppliers is not null)
			return _customAppliers.Keys;

		return [];
	}

	///<inheritdoc/>
	void IAggregate.ApplyEvent(object aggregateEvent, EventMetadata metadata)
	{
		if (Details.Locked)
			throw new LockedException(Details.Id);

		ApplyCore(aggregateEvent, metadata);
	}

#pragma warning restore CA1033 // Interface methods should be callable by child types

	void ApplyCore(object @event, EventMetadata metadata)
	{
		var applier = GetApplier(@event.GetType());
		applier(this, @event);

		if (metadata.AggregateVersion == 1)
			Details.Created = metadata.When;

		Details.Updated = metadata.When;
		Details.CurrentVersion = metadata.AggregateVersion;
	}

	Action<IAggregate, object> GetApplier(Type eventType)
	{
		if (_applierMap is not null && _applierMap.TryGetValue(eventType, out var applier))
			return applier;

		if (_customAppliers is not null && _customAppliers.TryGetValue(eventType, out applier))
			return applier;

		throw new UnregisteredEventException(eventType, this);
	}

	/// <summary>
	/// Builds the static applier map for an aggregate type on first use. Returns false when the type
	/// uses instance-bound custom appliers and must register per instance.
	/// </summary>
	static bool TryBuildStaticAppliers(Type aggregateType, out Dictionary<Type, Action<IAggregate, object>> map)
	{
		if (DynamicTypes.ContainsKey(aggregateType))
		{
			map = null!;
			return false;
		}

		lock (ApplierBuildLock)
		{
			if (StaticApplierMaps.TryGetValue(aggregateType, out map!))
				return true;

			if (DynamicTypes.ContainsKey(aggregateType))
			{
				map = null!;
				return false;
			}

			Dictionary<Type, Action<IAggregate, object>> candidate = new();
			s_applierBuildTarget = candidate;
			s_dynamicDetected = false;

			try
			{
				var scratch = (AggregateBase)RuntimeHelpers.GetUninitializedObject(aggregateType);
				RegisterSystemAppliers(candidate);
				scratch.RegisterEvents();
			}
			finally
			{
				s_applierBuildTarget = null;
			}

			if (s_dynamicDetected)
			{
				DynamicTypes[aggregateType] = true;
				map = null!;
				return false;
			}

			map = candidate;
			StaticApplierMaps[aggregateType] = map;
			return true;
		}
	}

	static readonly ConcurrentDictionary<
		(Type AggregateType, Type EventType),
		Action<IAggregate, object>
	> GeneratedAppliers = new();

	static Action<IAggregate, object> GetGeneratedApplier(Type aggregateType, Type eventType) =>
		GeneratedAppliers.GetOrAdd(
			(aggregateType, eventType),
			static key =>
			{
				var (aggType, evType) = key;
				var applyMethod =
					aggType.GetMethod(
						"Apply",
						BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
						null,
						[evType],
						null
					)
					?? throw new InvalidOperationException(
						$"Aggregate '{aggType}' has no generated Apply method for event '{evType}'."
					);

				var openDelegateType = typeof(Action<,>).MakeGenericType(aggType, evType);
				var openDelegate = applyMethod.CreateDelegate(openDelegateType);
				var wrapMethod = typeof(AggregateBase)
					.GetMethod(nameof(WrapGeneratedApplier), BindingFlags.NonPublic | BindingFlags.Static)!
					.MakeGenericMethod(aggType, evType);
				return (Action<IAggregate, object>)wrapMethod.Invoke(null, [openDelegate])!;
			}
		);

	static Action<IAggregate, object> WrapGeneratedApplier<TAggregate, TEvent>(Action<TAggregate, TEvent> openDelegate)
		where TAggregate : AggregateBase
		where TEvent : class => (aggregate, @event) => openDelegate((TAggregate)aggregate, (TEvent)@event);

	static readonly ConcurrentDictionary<Type, int> SchemaVersions = new();

	static int GetSchemaVersion(Type eventType) =>
		SchemaVersions.GetOrAdd(
			eventType,
			static type =>
			{
				var property = type.GetProperty(
					"SchemaVersion",
					BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
				);
				if (property is null || property.GetMethod is null)
					return 1;

				if (property.GetMethod.IsStatic)
					return (int)property.GetValue(null)!;

				var instance = RuntimeHelpers.GetUninitializedObject(type);
				return (int)property.GetValue(instance)!;
			}
		);
}
