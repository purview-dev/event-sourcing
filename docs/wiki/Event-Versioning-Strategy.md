# Event Versioning Strategy

Each persisted event row/document records `SchemaVersion`, `CorrelationId`, `CausationId`, and `UserId` separately
from its payload. `IdempotencyId`, aggregate version, timestamp, event name, and aggregate identity are likewise
first-class metadata. This allows Admin and history consumers to inspect an event envelope without deserializing
sensitive or obsolete payload JSON.

Legacy SQL rows are assigned schema version 1 by the metadata migration. Document and table providers also treat a
missing schema-version field as version 1. Correlation, causation, and user identifiers remain null when they were not
recorded by the original write; they are never inferred during a migration.

This document codifies the product-wide approach to event versioning and schema evolution across all Purview EventSourcing providers.

## Core Principles

1. **Events are append-only immutable facts.** Never change the meaning of persisted event data.
2. **SchemaVersion is the versioning contract.** Track breaking payload changes through the `SchemaVersion` property on event classes.
3. **Upcasting bridges payload versions.** When old events must hydrate into new event shapes, implement `IEventUpcaster<TSource, TTarget>`.
4. **Unknown events fail safely.** Providers return `UnknownEvent` when event types cannot be resolved or deserialized.
5. **All providers implement consistent replay semantics.** Replay-time upcasting is applied uniformly across SQL Server, Azure Storage, and MongoDB.

## When to Version vs. When to Rename

### Add a new property without versioning (additive change)
- Property is **optional** (nullable or has a default).
- Backward compatibility is preserved: old events deserialize successfully without the new field.
- **Example:** `CustomerRegistered` gains an optional `PhoneNumber` field; old events hydrate with `null` or `string.Empty`.
- **Action:** No `SchemaVersion` bump needed; no upcaster required.

### Increment SchemaVersion (breaking payload change)
- Property is **required** and has no safe default (e.g., changes meaning or becomes non-nullable).
- Property is **removed or renamed** without a clear mapping.
- **Example:** `OrderCreated` v1 has optional `Currency`; v2 makes it required. Or `Price` → `UnitPrice` with different semantics.
- **Action:** Use `[Event(Version = 2)]` or manually override `SchemaVersion => 2`. Implement an upcaster.

### Create a new event type (semantic change)
- The event's **meaning fundamentally changes** (e.g., `UserRegistered` → `UserRegisteredWithEmailVerification`).
- The domain concept is distinct and should have its own event class.
- **Example:** A new workflow requires user email verification at registration; instead of changing `UserRegistered`, define `UserRegisteredAndVerificationSent`.
- **Action:** Define a new event class. Optionally define an upcaster if the new event should apply the old event's data.

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

    // Version 1: original (Currency not present in old events)
    // public partial void Create(string orderId);

    // Version 2: Currency is now part of the event
    [Event(Version = 2)]
    public partial void Create(string orderId, string currency);
}
```

**Manually:**
```csharp
public sealed class OrderCreated : EventBase
{
    public string OrderId { get; set; } = default!;
    public string Currency { get; set; } = default!;

    public override int SchemaVersion => 2;

    protected override void BuildEventHash(ref HashCode hash)
    {
        hash.Add(OrderId);
        hash.Add(Currency);
    }
}
```

## Upcasting Chains

### Purpose
Upcasters convert old event payloads (deserialized from storage) into current event shapes so aggregates can apply them during replay.

### Implementation

**Single-hop upcaster (v1 → v2):**
```csharp
public sealed class OrderCreatedV1ToV2Upcaster
    : IEventUpcaster<OrderCreatedV1, OrderCreated>
{
    public OrderCreated Upcast(OrderCreatedV1 source) =>
        new()
        {
            Details = source.Details,  // Always preserve metadata
            OrderId = source.OrderId,
            Currency = "USD",           // Default for legacy events
        };
}
```

**Multi-hop chain (v1 → v2 → v3):**
```csharp
// Register both upcasters; the registry applies them in sequence.
services.AddEventUpcaster<OrderCreatedV1, OrderCreatedV2, OrderCreatedV1ToV2Upcaster>();
services.AddEventUpcaster<OrderCreatedV2, OrderCreated, OrderCreatedV2ToV3Upcaster>();

// On replay, events automatically: v1 → v2 → v3 (final) → aggregate.Apply()
```

### Upcaster Rules
- **Direction:** Forward only (v1 → v2 → v3 → …). Downgrading events is not supported.
- **Metadata:** Always copy `EventDetails` to the target event (idempotency, correlation, user, timestamp).
- **Legacy type resolution:** Legacy (source) event types are registered automatically from the upcaster registry when an aggregate is initialized, so stored legacy event names resolve back to CLR types during replay. No extra registration is required.
- **Same-type upcasters:** An upcaster whose source and target types are the same (an in-place transform) is applied exactly once; it is not treated as a cycle.
- **Cycle detection:** The registry detects and rejects circular upcaster chains (for example v1 → v2 → v1) when it is constructed.
- **Unknown target:** If an old event has no upcaster path to a known type, it remains `UnknownEvent`.

### Detecting Partial Replay

Because old consumers reading newer events skip what they cannot apply, a replayed aggregate can be **partially stale** without an error being thrown. Replay records every skipped event on the aggregate instance:

- `aggregate.SkippedEvents` (`IReadOnlyList<SkippedEventRecord>`) lists the versions, persisted event names, and whether each was unresolvable (`UnknownEvent`) or simply not applicable.
- Callers that must not act on stale state should check `SkippedEvents` after a load and fail closed or rehydrate through a different path when it is non-empty.

`SkippedEvents` is populated only while an aggregate is rehydrated from an event stream. It is not persisted in SQL Server/PostgreSQL EF-backed snapshot payloads, so always check it on the aggregate returned by an event-stream load.

Downgrading (downcasting newer events into older shapes) remains unsupported; this signal exists so applications can detect and react to the mixed-version-fleet case explicitly.

## Replay Semantics (All Providers)

When replaying an aggregate from the event stream:

1. **Deserialize** the event from JSON. If the event type cannot be resolved, return `UnknownEvent`.
2. **Apply upcasting chain** (if a registry is present). Follow all registered upcasters in sequence until no further upcaster is found.
3. **Call aggregate.ApplyEvent()** with the (possibly upcast) event.
4. **Handle unknown events** gracefully. The aggregate's `CanApplyEvent()` should return false for `UnknownEvent`; the store logs and continues replay.

### Provider Implementation Checklist
- [ ] `GetEventRangeAsync()` applies the upcaster registry after deserializing.
- [ ] `GetAsync()` (single aggregate load) applies the upcaster registry during replay.
- [ ] Unknown event types return `UnknownEvent` with metadata populated.
- [ ] Upcasting errors are logged and surfaced (not silently swallowed).
- [ ] Multi-hop upcasting chains are tested end-to-end.

## Documentation and Contracts

### EventDetails Preservation
- Always copy `EventDetails` in upcasters. These fields are critical for causation, correlation, and audit trails.
- Fields: `IdempotencyId`, `AggregateVersion`, `When`, `UserId`, `CausationId`, `CorrelationId`.

### Event Type Naming
- Event type names are persisted in the event store. Renaming an event type breaks deserialization without a migration step.
- If renaming is necessary, define the old event type alongside the new one and create an upcaster.

### Version Boundaries
- `SchemaVersion` on the event itself is serialized in the JSON payload.
- Consumers can inspect `event.SchemaVersion` to make conditional decisions during replay (fallback values, feature flags, etc.).

## Test Coverage

All providers must verify:

1. **Additive changes** – Old events deserialize and apply without upcasters.
2. **Versioned events** – New events with `SchemaVersion > 1` deserialize correctly.
3. **Single-hop upcasting** – V1 events are upcast to V2 during replay.
4. **Multi-hop upcasting** – V1 → V2 → V3 chains work end-to-end.
5. **Unknown events** – Missing event types produce `UnknownEvent` and replay continues.
6. **EventDetails preservation** – Metadata (idempotency, correlation, user) is copied in upcasters.
7. **Cycle detection** – Circular upcaster chains are rejected at registry construction.

## Related Files

- **Core abstractions:** `src/src/EventSourcing/Aggregates/Events/EventBase.cs`, `IEventUpcaster.cs`, `EventUpcasterRegistry.cs`
- **SQL Server replay:** `src/src/SqlServer/Events/SqlServerEventStore.GetEventRangeAsync.cs` (reference implementation)
- **Sample:** [Event-Versioning-Examples.md](Event-Versioning-Examples.md)
- **Tests:** Provider-specific replay tests (to be harmonized)

---

*Last Updated: 2026-07-30*
