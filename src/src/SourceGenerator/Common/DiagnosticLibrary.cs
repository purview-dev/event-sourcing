namespace Purview.EventSourcing.SourceGenerator.Common;

static class DiagnosticLibrary
{
	const string AggregateCategory = "Aggregates";
	const string EventContractCategory = "EventContracts";

	/// <summary> EVENTSTORE001: Aggregate must be partial </summary>
	public static readonly DiagnosticDescriptor AggregateMustBePartial = new(
		id: "EVENTSTORE001",
		title: "Aggregate must be partial",
		messageFormat: "Aggregate '{0}' must be declared partial to use [Aggregate]",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE002: Aggregate must inherit AggregateBase </summary>
	public static readonly DiagnosticDescriptor AggregateMustInheritAggregateBase = new(
		id: "EVENTSTORE002",
		title: "Aggregate must inherit AggregateBase",
		messageFormat: "Aggregate '{0}' must inherit from Purview.EventSourcing.Aggregates.AggregateBase to use [Aggregate]",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE003: Nested aggregates are not supported </summary>
	public static readonly DiagnosticDescriptor NestedAggregatesAreNotSupported = new(
		id: "EVENTSTORE003",
		title: "Nested aggregates are not supported",
		messageFormat: "Aggregate '{0}' cannot be nested inside another type when using [Aggregate]",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE004: Generic aggregates are not supported </summary>
	public static readonly DiagnosticDescriptor GenericAggregatesAreNotSupported = new(
		id: "EVENTSTORE004",
		title: "Generic aggregates are not supported",
		messageFormat: "Aggregate '{0}' cannot be generic when using [Aggregate]",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE005: RegisterEvents is generated automatically </summary>
	public static readonly DiagnosticDescriptor ManualRegisterEventsIsNotSupported = new(
		id: "EVENTSTORE005",
		title: "RegisterEvents is generated automatically",
		messageFormat: "Aggregate '{0}' cannot declare RegisterEvents() manually when using [Aggregate]",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE006: Event method requires Aggregate attribute </summary>
	public static readonly DiagnosticDescriptor EventMethodRequiresAggregateAttribute = new(
		id: "EVENTSTORE006",
		title: "Event requires Aggregate",
		messageFormat: "Method '{0}' must be declared on a [Aggregate] aggregate type",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE007: Event method must be partial </summary>
	public static readonly DiagnosticDescriptor EventMethodMustBePartial = new(
		id: "EVENTSTORE007",
		title: "Generated event method must be partial",
		messageFormat: "Method '{0}' must be declared partial to use [Event]",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE008: Unsupported generated event method signature </summary>
	public static readonly DiagnosticDescriptor UnsupportedEventMethodSignature = new(
		id: "EVENTSTORE008",
		title: "Unsupported generated event method signature",
		messageFormat: "Method '{0}' has an unsupported [Event] signature: {1}",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE009: Generated event names must be unique </summary>
	public static readonly DiagnosticDescriptor DuplicateGeneratedEventName = new(
		id: "EVENTSTORE009",
		title: "Generated event names must be unique",
		messageFormat: "Method '{0}' on aggregate '{1}' conflicts with another [Event] method because both would generate the event type '{2}'",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor EventParameterMustMapToWritableProperty = new(
		id: "EVENTSTORE010",
		title: "Generated event parameters must map to writable aggregate properties",
		messageFormat: "Parameter '{0}' on method '{1}' must map to a writable aggregate property on '{2}': {3}",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor AggregatePropertySetterShouldBePrivate = new(
		id: "EVENTSTORE011",
		title: "Aggregate property setters should be private",
		messageFormat: "Aggregate property '{0}' on '{1}' has a non-private setter ('{2}'). Event-sourced aggregate state should use private setters.",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor AggregatePropertyCollectionTypeMustUseEventStoreCollections = new(
		id: "EVENTSTORE018",
		title: "Aggregate collection properties must use EventStore collections",
		messageFormat: "Aggregate property '{0}' on '{1}' has unsupported collection type '{2}'. Collection and array properties must use Purview.EventSourcing.EventStoreList<T> or Purview.EventSourcing.EventStoreSet<T>.",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor NullableScalarEqualityNullComparisonShouldUsePatternMatching = new(
		id: "EVENTSTORE019",
		title: "Use pattern matching for nullable scalar null checks",
		messageFormat: "Nullable scalar value object comparison '{0}' can trigger CS9342 due to overloaded equality operators. Use '{1}' instead.",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ScalarComplexValueMayNotTranslateInSQLSnapshots = new(
		id: "EVENTSTORE020",
		title: "Complex scalar Value paths may not translate in SQL snapshot queries",
		messageFormat: "Aggregate property '{0}' on '{1}' is a [Scalar] whose Value type '{2}' is complex. Deep SQL predicates through '.Value' are typically non-translatable; prefer a computed mirror property for query scenarios.",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor EventSchemaVersionMustBePositive = new(
		id: "EVENTSTORE021",
		title: "Event schema version must be positive",
		messageFormat: "Method '{0}' on aggregate '{1}' declares schema version '{2}'. Schema versions must be greater than or equal to 1.",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor DuplicateEventSchemaVersionOnAggregate = new(
		id: "EVENTSTORE022",
		title: "Duplicate event schema version on aggregate",
		messageFormat: "Method '{0}' on aggregate '{1}' reuses schema version '{2}', which is already used by method '{3}'. Event schema versions on a single aggregate must be unique.",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor AggregateMethodShouldBeVerbPhrase = new(
		id: "EVENTSTORE012",
		title: "Aggregate methods should be verb phrases",
		messageFormat: "Method '{0}' does not appear to be a verb phrase. Aggregate mutation methods should describe an action, for example 'ChangeName', 'RegisterCustomer', or 'Deactivate'.",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor EventParameterNullabilityMismatch = new(
		id: "EVENTSTORE016",
		title: "Event parameter nullability differs from aggregate property",
		messageFormat: "Parameter '{0}' on '{1}' is non-nullable but maps to nullable aggregate property '{2}'. Consider declaring the parameter as '{3}' to match the property and avoid generated CS8600 warnings.",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Info,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ComputedParameterCannotBeSetByCaller = new(
		id: "EVENTSTORE017",
		title: "Computed parameter cannot be set by caller",
		messageFormat: "Method '{0}' cannot set computed parameter '{1}'. Omit this argument so the aggregate computes it.",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor EventNameShouldBePastTense = new(
		id: "EVENTSTORE013",
		title: "Event names should be past tense",
		messageFormat: "Event name '{0}' does not appear to be a past-tense fact. Events should describe something that has happened, for example 'NameChanged' or 'CustomerRegistered'.",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor EventNameOverrideShouldBePastTense = new(
		id: "EVENTSTORE014",
		title: "Event name overrides should be past tense",
		messageFormat: "Event name override '{0}' on method '{1}' does not appear to be a past-tense fact",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor UnableToInferEventName = new(
		id: "EVENTSTORE015",
		title: "Unable to infer a past-tense event name",
		messageFormat: "Unable to infer a past-tense event name for method '{0}'. Rename the method to a verb phrase such as '{1}', or add an explicit event name override.",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor AggregateBaseReferenceMissing = new(
		id: "EVENTSTORE110",
		title: "Unable to find reference to AggregateBase",
		messageFormat: "Add a reference to the NuGet package containing Purview.EventSourcing",
		category: AggregateCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE030: Aggregate event contract removed or renamed </summary>
	public static readonly DiagnosticDescriptor EventContractRemoved = new(
		id: "EVENTSTORE030",
		title: "Aggregate event contract removed or renamed",
		messageFormat: "Aggregate '{0}' event contract was removed or renamed relative to the approved baseline. Existing streams reference this aggregate type; retain the type, or migrate existing streams before removing it.",
		category: EventContractCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE031: Event removed or renamed </summary>
	public static readonly DiagnosticDescriptor EventContractEventRemoved = new(
		id: "EVENTSTORE031",
		title: "Event removed or renamed",
		messageFormat: "Event '{0}' (schema version {1}) on aggregate '{2}' was removed or renamed relative to the approved baseline. Removing an event type breaks replay of existing streams; retain the event, or add an upcaster.",
		category: EventContractCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE032: Persisted field removed or renamed </summary>
	public static readonly DiagnosticDescriptor EventContractFieldRemoved = new(
		id: "EVENTSTORE032",
		title: "Persisted event field removed or renamed",
		messageFormat: "Field '{0}' was removed from event '{1}' (schema version {2}) on aggregate '{3}' relative to the approved baseline. Removing a persisted field changes the serialized payload; retain the field, or bump the schema version and add an upcaster.",
		category: EventContractCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE033: Persisted field type changed incompatibly </summary>
	public static readonly DiagnosticDescriptor EventContractFieldTypeChanged = new(
		id: "EVENTSTORE033",
		title: "Persisted event field type changed",
		messageFormat: "Field '{0}' on event '{1}' (schema version {2}) on aggregate '{3}' changed type from '{4}' to '{5}' relative to the approved baseline. The persisted JSON shape is no longer compatible; restore the type, or bump the schema version and add an upcaster.",
		category: EventContractCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE034: Persisted field became required or non-nullable </summary>
	public static readonly DiagnosticDescriptor EventContractFieldBecameRequired = new(
		id: "EVENTSTORE034",
		title: "Persisted event field became required or non-nullable",
		messageFormat: "Field '{0}' on event '{1}' (schema version {2}) on aggregate '{3}' became required or non-nullable relative to the approved baseline. Older payloads may omit or null this field; keep it optional, or bump the schema version and add an upcaster.",
		category: EventContractCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE035: Event schema version regressed or conflicts with the baseline </summary>
	public static readonly DiagnosticDescriptor EventContractSchemaVersionRegression = new(
		id: "EVENTSTORE035",
		title: "Event schema version regressed",
		messageFormat: "Event '{0}' on aggregate '{1}' declares schema version {2}, which is lower than the approved baseline version {3}. Schema versions must never decrease; restore the previous version, or introduce a new event type.",
		category: EventContractCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE036: Baseline manifest is malformed or unsupported </summary>
	public static readonly DiagnosticDescriptor EventContractBaselineMalformed = new(
		id: "EVENTSTORE036",
		title: "Event contract baseline is malformed",
		messageFormat: "The event contract baseline file '{0}' could not be used: {1}. Regenerate the baseline with PurviewEventContractManifestEnabled=true, or fix the committed baseline.",
		category: EventContractCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	/// <summary> EVENTSTORE037: Event type not marked with [EventContract] </summary>
	public static readonly DiagnosticDescriptor EventTypeShouldBeMarkedWithEventContract = new(
		id: "EVENTSTORE037",
		title: "Event type must be marked with [EventContract]",
		messageFormat: "Event type '{0}' must be marked with the [EventContract] attribute",
		category: EventContractCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);
}
