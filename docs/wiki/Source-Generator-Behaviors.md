# Source Generator Behaviors

This page documents framework-level source-generator behavior (not storage-provider behavior).

## Aggregate eligibility and inheritance

`[Aggregate]` supports three inheritance paths:

1. No declared base class: generated partial type automatically inherits `AggregateBase`.
2. Direct inheritance from `AggregateBase`.
3. Transitive inheritance through one or more intermediate base classes.

Other eligibility rules:

- Aggregate type must be `partial`.
- Nested and generic aggregate types are not supported.
- `RegisterEvents()` is generated and cannot be manually declared.

### Inheritance examples

```csharp
// 1) No declared base class (generator adds AggregateBase on generated partial)
[Aggregate]
public partial class ProductAggregate
{
    [Event]
    public partial void Create(string name);
}

// 2) Direct inheritance
[Aggregate]
public partial class OrderAggregate : AggregateBase
{
    [Event]
    public partial void CreateOrder(string customerId);
}

// 3) Transitive inheritance
public abstract class DomainAggregateBase : AggregateBase { }
public abstract class BillingAggregateBase : DomainAggregateBase { }

[Aggregate]
public partial class InvoiceAggregate : BillingAggregateBase
{
    [Event]
    public partial void CreateInvoice(string invoiceNumber);
}
```

## Generated event naming and namespace

Default event namespace:

- `<AggregateNamespace>.<AggregateNameWithoutSuffix>Events`
- Example: `Testing.OrderAggregate` -> `Testing.OrderEvents`

Default event type naming:

- Event names are inferred from method names (or overridden with `EventName = ...`).
- Event type suffix defaults to `Event` (configurable with `EventSuffix` defaults/overrides).
- Typical generated type: `Testing.OrderEvents.OrderCreatedEvent`.

Namespace can be overridden per method (`EventNamespace`) or by aggregate defaults.

### Event naming examples

```csharp
namespace Testing;

[Aggregate]
public partial class OrderAggregate : AggregateBase
{
    [Event]
    public partial void CreateOrder(string customerId);

    [Event(EventName = "OrderRegistered", EventNamespace = "Testing.Custom.Events")]
    public partial void RegisterOrder(string customerId);
}
```

Typical generated types:

- `Testing.OrderEvents.OrderCreatedEvent` (default namespace/name)
- `Testing.Custom.Events.OrderRegistered` (explicit namespace/name)

> [!NOTE]
> An explicit `EventName` is used verbatim: the generator does **not** append the `Event` suffix when
> a name is provided. Include the suffix in the explicit name (for example `EventName =
> "OrderRegisteredEvent"`) if you want the generated type to end in `Event`. The `Event` suffix is
> only appended to inferred names.

## Hook behavior semantics

Property hooks are property-scoped:

- `On<Property>Changing(ref value)` runs on generated command methods before event creation.
- `On<Property>Changed(previous, current)` runs in generated `Apply(...)` after assignment.
- If different events update the same property, the same property hooks run for each.
- Hooks run only when the event method maps that property.

Replay behavior:

- Replay executes generated `Apply(...)`.
- `On<Property>Changed` runs on replay.
- `On<Property>Changing` does not run on replay.

Event hooks are event-scoped:

- `OnRaising<EventName>Event(ref ...)`
- `OnRaised<EventName>Event(@event)`
- `OnApplied<EventName>Event(@event)`
- `OnShouldApply<EventName>Event(@event, ref bool shouldApply)`

Manual behavior:

- `Manual = true` does not auto-wire property hooks unless manual code invokes them.

### Property hook example

```csharp
[Aggregate]
public partial class CustomerAggregate : AggregateBase
{
    public string Email { get; private set; } = string.Empty;

    [Event(EventName = "CustomerRegistered")]
    public partial void Register(string email);

    [Event(EventName = "CustomerEmailChanged")]
    public partial void ChangeEmail(string email);

    partial void OnEmailChanging(ref string email) => email = email.Trim().ToLowerInvariant();
    partial void OnEmailChanged(string previous, string current) { /* audit */ }
}
```

`OnEmailChanging/Changed` run for both `Register` and `ChangeEmail` because both map to `Email`.

## Event method mapping and validation

- `[Event]` methods must be `partial` declarations without bodies.
- Return types must be `void`, `bool`, or the containing aggregate type.
- Parameters must map to writable aggregate properties unless explicitly handled as metadata/manual payload.
- Collection event methods (`[CollectionEvent]`) require `EventStoreList<T>` / `EventStoreSet<T>` target properties.

### Event contracts

Events are emitted as `[EventContract]` `sealed record` types — pure payload data with no base class
or interface:

```csharp
[EventContract]
public sealed record OrderCreatedEvent
{
    public static int SchemaVersion => 1;
    [JsonIgnore] public EventMetadata Metadata { get; set; }
    public string CustomerId { get; set; }
}
```

- `[EventContract]` marks the type as an event contract (the generator, analyzer, and upcasting
  registry use it to recognise event types; hand-written events registered via
  `Register<TEvent>`/`RegisterGenerated<TEvent>` must be marked with it too — `EVENTSTORE037`).
- `Metadata` (`EventMetadata`, a readonly record struct) carries framework-managed metadata
  (aggregate version, timestamp, schema version, idempotency/correlation/causation/user ids). It is
  `[JsonIgnore]`d, so event payloads no longer embed metadata; providers persist metadata to row
  columns and rehydrate it on replay.
- `GetHashCode` is content-based for payload properties and metadata, preserving stable event hashing
  (used by the Azure idempotency compound key).
- The generated `RegisterEvents` registers appliers with `RegisterGenerated<TEvent>()`, which resolves
  the generated `Apply(TEvent)` method once per aggregate/event type and shares it statically, so
  aggregate construction allocates no per-instance applier delegates.

### Generated command method shape

A generated command method constructs **one** event instance, runs the property `On<Property>Changing`
hooks, evaluates `OnShouldApply` before `OnRaising`, runs `OnRaising`/`OnComputing` hooks (which may
mutate parameters via `ref`), re-synchronizes the event's payload properties from the post-hook
values, re-evaluates `OnShouldApply`, then records the event via `RecordAndApply`. The single
allocation keeps command invocation allocation-light; the post-hook re-synchronization preserves the
exact payload values that a second construction would have produced.

### Example

```csharp
[Aggregate]
public partial class ReportAggregate : AggregateBase
{
    public EventStoreSet<string> Tags { get; private set; } = [];

    [CollectionEvent(nameof(Tags))]
    public partial void AddTag(string tag);
}
```

### Parameter nullability and required guards

The generator honors two standard attributes on event parameters to tighten command-time validation and the shape of the
generated event class:

- `[NotNull]` (`System.Diagnostics.CodeAnalysis`) on a nullable parameter generates an `ArgumentNullException` guard and
  emits the event property as non-nullable.
- `[Required]` (`System.ComponentModel.DataAnnotations`) on a nullable `string` parameter generates an
  `ArgumentException` guard for null or whitespace and emits the event property as non-nullable.

Both attributes also cause the generator to use a local copy of the parameter value when calling `On...Changing` hooks
and when creating the event. This keeps the original parameter unmodified so the compiler does not require it to be
assigned after a `throw` path.

```csharp
[Aggregate]
public partial class ProfileAggregate : AggregateBase
{
    public string? Bio { get; private set; }

    [Event]
    public partial void UpdateBio([NotNull] string? bio);
}
```

For the event above, the generator produces a property typed as `string` rather than `string?`. The
generated record carries the `[EventContract]` attribute and the usual `Metadata`/`SchemaVersion`
members (see the event-contract shape above); it has **no base class**:

```csharp
[EventContract]
public sealed record BioUpdatedEvent
{
    public string Bio { get; set; } = default!;
}
```

## Value-object conversion behavior

> The `[Scalar]` / `[ValueObject]` generator and analyzer are provided by the `Purview.ValueObjects` package,
> referenced transitively by `Purview.EventSourcing`. Value objects live in the `Purview.ValueObjects` and
> `Purview.ValueObjects.Serialization` namespaces (previously `Purview.EventSourcing.ValueObjects` and
> `Purview.EventSourcing.Serialization`).

- Generated mapping paths use `Create(...)` semantics for strict command-time conversion/validation.
- Contextual `Create(TValue, in ValueObjectContext<TAggregate>)` is used when available.
- Replay/hydration paths apply event payloads through generated `Apply(...)` logic.
- Snapshot-query translation depends on how the provider maps the resulting property graph, not only on the value-object
  generator behavior.
- Projects compiled with the SQL Server or PostgreSQL EF analyzer can mark a property `[EfOpaque]`. The EF-only
  generator emits this internal marker into the consuming compilation; it does not add a runtime attribute API.
- `EVENTSTOREEF001` reports dictionary-like members reachable from an aggregate unless they are explicitly opaque.
  Prefer a collection of domain entry objects when structural querying is required; the generator does not synthesize
  those domain types.
- `EVENTSTOREEF002` reports uses of an opaque member in recognized snapshot query expressions. Opaque values round-trip
  through JSON but their contents are not part of EF's queryable complex model.
- A `[Scalar]` value object that wraps a complex CLR type may serialize correctly while still requiring a separate
  directly mapped complex mirror property for deep SQL predicates.

### Value-object conversion examples

```csharp
// Scalar conversion
[Scalar]
public readonly partial record struct EmailAddress
{
    public string Value { get; }
    static partial void OnNormalize(ref string value) => value = value.Trim().ToLowerInvariant();
    static partial void OnValidate(string value) { /* format checks */ }
}
```

```csharp
// Contextual conversion
[Scalar]
public readonly partial record struct OrderStatus
    : IContextualValueObject<OrderStatus, OrderStatusCode, OrderAggregate>
{
    public OrderStatusCode Value { get; }

    public static OrderStatus Create(OrderStatusCode value, in ValueObjectContext<OrderAggregate> context)
        => IsValidTransition(context.Aggregate.Status.Value, value)
            ? new(value)
            : throw new InvalidOperationException();
}
```

## Diagnostics to expect

Model-validation diagnostics are produced by `Purview.EventSourcing.SourceGenerator` analyzers
(`AggregateDiagnosticAnalyzer`, `ValueObjectDiagnosticAnalyzer`, and `EventStoreAnalyzer`), not by
the source generators themselves. The generators consume the same validation internally to decide
whether to emit source, but they do not report these validation diagnostics. Analyzer diagnostics
can be suppressed or configured through the usual `#pragma warning` / `.editorconfig` mechanisms.

The exception is the event-contract manifest baseline comparison: the generator itself reports
`EVENTSTORE030`–`EVENTSTORE036` when the current contracts differ from the committed baseline (see
[Event Contract Manifest](Event-Contract-Manifest.md)). Those diagnostics are emitted from the
generator's output stage, so they always surface on a build regardless of analyzer configuration.

Common aggregate diagnostic IDs:

- `EVENTSTORE001` aggregate must be partial
- `EVENTSTORE002` aggregate must inherit `AggregateBase` (or have no base so generator can add it)
- `EVENTSTORE003` nested aggregates unsupported
- `EVENTSTORE004` generic aggregates unsupported
- `EVENTSTORE005` manual `RegisterEvents` unsupported
- `EVENTSTORE007` generated event method must be partial
- `EVENTSTORE009` duplicate generated event names
- `EVENTSTORE010` parameter must map to writable property
- `EVENTSTORE018` unsupported aggregate collection property type
- `EVENTSTORE021` event schema version must be positive
- `EVENTSTORE022` duplicate event schema version on aggregate

Value-object diagnostics are provided by the `Purview.ValueObjects` package (the `[Scalar]`/`[ValueObject]`
generator and analyzer moved out of this repository). They use the `VO1001`–`VO1008` ID range; see the
`Purview.ValueObjects` package documentation for the full list.

The analyzer and the generator share the same validation rules (the model builders are the single source
of truth). When validation fails, the generator skips generation entirely — it never emits an invalid
partial type — while the analyzer reports the diagnostic. A generator-only run therefore produces no
output and no exception for invalid input; the validation diagnostics are always surfaced by the analyzer
assets that ship in the same package. The manifest-compatibility diagnostics (`EVENTSTORE030`–`036`), by
contrast, are reported by the generator itself.

## Testing generated output

Generator unit tests assert on the generated structure with the `CodeQuery` API from
`Purview.SourceGeneratorFramework.Testing` rather than whole-file string matching:

- `result.Generated()` returns a `CodeQuery` over the generated trees (backed by the output compilation).
- Prefer `GetClass`/`GetRecord`/`GetStruct`/`GetEnum`/`HasNamespace`, `HasMethod`, `HasProperty`,
  `HasConstructor`, and `TypeReference`-based parameter matching for member signatures.
- Keep string assertions only for method-body statements that `CodeQuery` does not model (for example
  `RecordAndApply(@event);`), scoped to the returned syntax node's body.
- Operator declarations are `OperatorDeclarationSyntax`, not methods; assert them via
  `CodeQuery.GetOperator`/`HasOperator`/`TryGetOperator` (optionally scoped with
  `CodeQuery.In(type)`), or `GetConversionOperator` for `implicit`/`explicit` conversions.

Incremental caching is tested with the framework's `GenerateIncrementalAsync`/`RunIncrementalAsync`, which
reuse one driver and compilation across identical runs. The framework-named stages
(`GetGenerationConfiguration`, `GetGenerationContext_{Capabilities}`, and the per-target
`ForAttribute`/target stage) must stay `Cached`/`Unchanged` on identical reruns, and only the stage whose
input actually changed reports `Modified`.
