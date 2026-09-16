# Event Versioning Strategy

Each persisted event row/document records `SchemaVersion`, `CorrelationId`, `CausationId`, `UserId`,
`IdempotencyId`, aggregate version, timestamp, and event name separately from its payload. This
allows Admin and history consumers to inspect an event envelope without deserializing sensitive or
obsolete payload JSON.

Legacy SQL rows are assigned schema version 1 by the metadata migration. Document and table providers
also treat a missing schema-version field as version 1. Correlation, causation, and user identifiers
remain null when they were not recorded by the original write; they are never inferred during a
migration.

This document codifies the product-wide approach to event versioning and schema evolution across all
Purview EventSourcing providers.

## Core Principles

1. **Events are append-only immutable facts.** Never change the meaning of persisted event data.
2. **SchemaVersion is the versioning contract.** Track breaking payload changes through the
   `SchemaVersion` on the event contract (a static property on generated events).
3. **Upcasting bridges payload versions.** When old events must hydrate into new event shapes,
   implement `IEventUpcaster<TSource, TTarget>`.
4. **Unknown events fail safely.** Providers return `UnknownEvent` when event types cannot be
   resolved or deserialized.
5. **Providers implement consistent replay semantics.** Replay-time upcasting is applied uniformly by
   the stream-backed providers — SQL Server, PostgreSQL, Azure Storage, and MongoDB. The in-memory
   provider does not apply upcasting.

## Event Contract Shape

Events are `[EventContract]` sealed records with no base class or interface. Payload properties are
stored directly on the record; framework-managed metadata lives in a `[JsonIgnore]`d `Metadata`
property of type `EventMetadata` and is persisted by providers to row columns / document fields and
rehydrated on replay.

```csharp
[EventContract]
public sealed record OrderCreatedEvent
{
    public string OrderId { get; set; } = default!;
    public string Currency { get; set; } = default!;

    [JsonIgnore]
    public EventMetadata Metadata { get; set; }

    public static int SchemaVersion => 2;
}
```

See [Source Generator Behaviors](Source-Generator-Behaviors.md) for the generated shape and naming
rules.

## When to Version vs. When to Rename

### Add a new property without versioning (additive change)

- Property is **optional** (nullable or has a default).
- Backward compatibility is preserved: old events deserialize successfully without the new field.
- **Example:** `CustomerRegisteredEvent` gains an optional `PhoneNumber` field; old events hydrate
  with `null` or `string.Empty`.
- **Action:** No `SchemaVersion` bump needed; no upcaster required.

### Increment SchemaVersion (breaking payload change)

- Property is **required** and has no safe default (e.g., changes meaning or becomes non-nullable).
- Property is **removed or renamed** without a clear mapping.
- **Example:** `OrderCreatedEvent` v1 has optional `Currency`; v2 makes it required. Or `Price` →
  `UnitPrice` with different semantics.
- **Action:** Use `[Event(Version = 2)]` and implement an upcaster.

### Create a new event type (semantic change)

- The event's **meaning fundamentally changes** (e.g., `UserRegisteredEvent` →
  `UserRegisteredWithEmailVerificationEvent`).
- The domain concept is distinct and should have its own event contract.
- **Example:** A new workflow requires user email verification at registration; instead of changing
  `UserRegisteredEvent`, define `UserRegisteredAndVerificationSentEvent`.
- **Action:** Define a new event class. Optionally define an upcaster if the new event should apply
  the old event's data.

## SchemaVersion Details

### Scope

- `SchemaVersion` is per-event-class, not per-aggregate.
- Multiple events on one aggregate can have different versions.

### Numbering

- Starts at 1 (default).
- Increment by 1 for each breaking change.
- Never decrease; version numbers are immutable markers.

### Declaration

**Via the source generator:**

```csharp
[Aggregate]
public partial class OrderAggregate : AggregateBase
{
    public string OrderId { get; private set; } = default!;
    public string Currency { get; private set; } = "USD";  // Added in v2

    // Version 2: Currency is now part of the event
    [Event(Version = 2)]
    public partial void CreateOrder(string orderId, string currency);
}
```

The generator emits a `[EventContract]` record named `OrderCreatedEvent` with
`public static int SchemaVersion => 2;`.

**Manually (hand-written event contract):**

```csharp
[EventContract]
public sealed record OrderCreatedEvent
{
    public string OrderId { get; set; } = default!;
    public string Currency { get; set; } = default!;

    public static int SchemaVersion => 2;
}
```

Hand-written events must be marked `[EventContract]` (the analyzer enforces this with
`EVENTSTORE037`) and are registered with `Register<TEvent>(...)` or `RegisterGenerated<TEvent>()`.

## Upcasting Chains

### Purpose

Upcasters convert old event payloads (deserialized from storage) into current event shapes so
aggregates can apply them during replay.

### Implementation

**Single-hop upcaster (v1 → v2):**

```csharp
public sealed class OrderCreatedV1ToV2Upcaster
    : IEventUpcaster<OrderCreatedEventV1, OrderCreatedEvent>
{
    public OrderCreatedEvent Upcast(OrderCreatedEventV1 source) =>
        new()
        {
            OrderId = source.OrderId,
            Currency = "USD",           // Default for legacy events
        };
}
```

Metadata is **not** copied by the upcaster: `EventMetadata` is carried by the framework and
re-attached by the store, so the source's metadata flows to the upcast event automatically.

**Multi-hop chain (v1 → v2 → v3):**

```csharp
// Register both upcasters; the registry applies them in sequence.
services.AddEventUpcaster<OrderCreatedEventV1, OrderCreatedEventV2, OrderCreatedV1ToV2Upcaster>();
services.AddEventUpcaster<OrderCreatedEventV2, OrderCreatedEvent, OrderCreatedV2ToV3Upcaster>();

// On replay, events automatically: v1 → v2 → v3 (final) → aggregate.ApplyEvent()
```

### Upcaster Rules

- **Direction:** Forward only (v1 → v2 → v3 → …). Downgrading events is not supported.
- **Metadata:** Do **not** copy metadata in an upcaster. The store re-attaches `EventMetadata`
  (idempotency, correlation, user, timestamp, schema version) to the upcast event.
- **Legacy type resolution:** Legacy (source) event types are registered automatically from the
  upcaster registry when an aggregate is initialized, so stored legacy event names resolve back to
  CLR types during replay. No extra registration is required.
- **Same-type upcasters:** An upcaster whose source and target types are the same (an in-place
  transform) is applied exactly once; it is not treated as a cycle.
- **Cycle detection:** The registry detects and rejects circular upcaster chains (for example
  v1 → v2 → v1) when it is constructed.
- **Unknown target:** If an old event has no upcaster path to a known type, it remains
  `UnknownEvent`.

### Detecting Partial Replay

Because old consumers reading newer events skip what they cannot apply, a replayed aggregate can be
**partially stale** without an error being thrown. Replay records every skipped event on the
aggregate instance:

- `aggregate.SkippedEvents` (`IReadOnlyList<SkippedEventRecord>`) lists the versions, persisted
  event names, and whether each was unresolvable (`UnknownEvent`) or simply not applicable.
- Callers that must not act on stale state should check `SkippedEvents` after a load and fail closed
  or rehydrate through a different path when it is non-empty.

`SkippedEvents` is populated only while an aggregate is rehydrated from an event stream. It is not
persisted in SQL Server/PostgreSQL EF-backed snapshot payloads, so always check it on the aggregate
returned by an event-stream load.

Downgrading (downcasting newer events into older shapes) remains unsupported; this signal exists so
applications can detect and react to the mixed-version-fleet case explicitly.

## Replay Semantics (Stream-Backed Providers)

When replaying an aggregate from the event stream:

1. **Deserialize** the event from JSON. If the event type cannot be resolved, return `UnknownEvent`.
2. **Apply upcasting chain** (if a registry is present). Follow all registered upcasters in sequence
   until no further upcaster is found.
3. **Call `aggregate.ApplyEvent()`** with the (possibly upcast) event.
4. **Handle unknown events** gracefully. The aggregate's `CanApplyEvent()` should return false for
   `UnknownEvent`; the store logs and continues replay.

### Provider Implementation Checklist

- [ ] `GetEventRangeAsync()` applies the upcaster registry after deserializing (SQL Server,
      PostgreSQL, Azure Storage, MongoDB).
- [ ] `GetAsync()` (single aggregate load) applies the upcaster registry during replay.
- [ ] Unknown event types return `UnknownEvent` with metadata populated.
- [ ] Upcasting errors are logged and surfaced (not silently swallowed).
- [ ] Multi-hop upcasting chains are tested end-to-end.

## Documentation and Contracts

### EventMetadata Preservation

`EventMetadata` is framework-managed and is **not** copied by upcasters. Providers persist these
fields to row columns / document fields and re-attach them on replay:

`IdempotencyId`, `AggregateVersion`, `When`, `UserId`, `CausationId`, `CorrelationId`,
`SchemaVersion`.

### Event Type Naming

- Event type names are persisted as `{aggregate-type}.{event-name-without-event-suffix}` (for
  example `order.order-created`). Renaming an event type breaks deserialization without a migration
  step.
- If renaming is necessary, define the old event type alongside the new one and create an upcaster.

### Version Boundaries

- `SchemaVersion` is persisted as event metadata (a row column / document field), not inside the
  payload, and is rehydrated into `EventMetadata` on replay.
- Consumers can inspect `@event.Metadata.SchemaVersion` to make conditional decisions during replay
  (fallback values, feature flags, etc.).

## Test Coverage

All stream-backed providers must verify:

1. **Additive changes** – Old events deserialize and apply without upcasters.
2. **Versioned events** – New events with `SchemaVersion > 1` deserialize correctly.
3. **Single-hop upcasting** – V1 events are upcast to V2 during replay.
4. **Multi-hop upcasting** – V1 → V2 → V3 chains work end-to-end.
5. **Unknown events** – Missing event types produce `UnknownEvent` and replay continues.
6. **Metadata preservation** – The store re-attaches `EventMetadata` (idempotency, correlation,
   user) after upcasting.
7. **Cycle detection** – Circular upcaster chains are rejected at registry construction.

## Related Files

- **Core abstractions:** `src/src/EventSourcing/Aggregates/Events/EventContractAttribute.cs`,
  `EventMetadata.cs`, `src/src/EventSourcing/Aggregates/Events/Upcasting/IEventUpcaster.cs`,
  `EventUpcasterRegistry.cs`
- **SQL Server replay:** `src/src/SqlServer/Events/SqlServerEventStore.GetEventRangeAsync.cs`
  (reference implementation)
- **Sample:** [Event-Versioning-Examples.md](Event-Versioning-Examples.md)
- **Tests:** Provider-specific replay tests (to be harmonized)