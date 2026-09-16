# Event Versioning: Practical Examples

This guide provides practical examples of implementing event versioning in Purview EventSourcing.

## Table of Contents

1. [Additive Changes (No Versioning Needed)](#additive-changes)
2. [Versioning with SchemaVersion](#versioning-with-schemaversion)
3. [Single-Hop Upcasting](#single-hop-upcasting)
4. [Multi-Hop Upcasting Chains](#multi-hop-upcasting-chains)
5. [Common Mistakes & How to Avoid Them](#common-mistakes)
6. [Testing Versioned Events](#testing-versioned-events)

## Additive Changes

When you add a new optional field to an event, no versioning is needed. Old events will deserialize
successfully with the new field set to its default value.

### Example: Adding an Optional Phone Number

**Initial event (v1, implicit `SchemaVersion = 1`):**

```csharp
[EventContract]
public sealed record CustomerRegisteredEvent
{
    public string CustomerId { get; set; } = default!;
    public string Email { get; set; } = default!;
}
```

**After adding an optional field (still v1, no SchemaVersion bump needed):**

```csharp
[EventContract]
public sealed record CustomerRegisteredEvent
{
    public string CustomerId { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? PhoneNumber { get; set; }  // Optional, new field
}
```

**Aggregate apply logic:**

```csharp
protected override void RegisterEvents()
{
    Register<CustomerRegisteredEvent>(cr =>
    {
        CustomerId = cr.CustomerId;
        Email = cr.Email;
        PhoneNumber = cr.PhoneNumber ?? "N/A";
    });
}
```

Old events will deserialize with `PhoneNumber = null`, and the aggregate handles it gracefully.

---

## Versioning with SchemaVersion

When you make a **breaking change** to an event's payload (required field added, meaning changed,
property removed), bump the `SchemaVersion`.

### Example: Making Phone Number Required

**Old event (v1):**

```csharp
[EventContract]
public sealed record CustomerRegisteredEvent
{
    public string CustomerId { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? PhoneNumber { get; set; }  // Was optional
}
```

**New event (v2, breaking change):**

```csharp
[EventContract]
public sealed record CustomerRegisteredEvent
{
    public string CustomerId { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string PhoneNumber { get; set; } = default!;  // Now required

    public static int SchemaVersion => 2;
}
```

### Defining the Upcaster

A same-type upcaster transforms the payload in place. The store re-attaches `EventMetadata` after
upcasting, so the upcaster only maps payload fields.

```csharp
public sealed class CustomerRegisteredV1ToV2Upcaster
    : IEventUpcaster<CustomerRegisteredEvent, CustomerRegisteredEvent>
{
    public CustomerRegisteredEvent Upcast(CustomerRegisteredEvent source) =>
        new()
        {
            CustomerId = source.CustomerId,
            Email = source.Email,
            PhoneNumber = source.PhoneNumber ?? "UNKNOWN",  // Default for old events
        };
}
```

---

## Single-Hop Upcasting

Single-hop upcasting converts v1 events directly to v2 during replay.

### Full Example: Order Events

**Step 1: Define the events**

```csharp
// Order event v1 (no currency)
[EventContract]
public sealed record OrderCreatedEventV1
{
    public string OrderId { get; set; } = default!;
    public decimal Amount { get; set; }
}

// Order event v2 (with currency, breaking change)
[EventContract]
public sealed record OrderCreatedEvent
{
    public string OrderId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;

    public static int SchemaVersion => 2;
}
```

**Step 2: Define the upcaster**

```csharp
public sealed class OrderCreatedV1ToV2Upcaster
    : IEventUpcaster<OrderCreatedEventV1, OrderCreatedEvent>
{
    public OrderCreatedEvent Upcast(OrderCreatedEventV1 source) =>
        new()
        {
            OrderId = source.OrderId,
            Amount = source.Amount,
            Currency = "USD",  // Default currency for old events
        };
}
```

**Step 3: Register the upcaster in DI**

```csharp
services.AddEventUpcaster<OrderCreatedEventV1, OrderCreatedEvent, OrderCreatedV1ToV2Upcaster>();
```

**Step 4: Use in the aggregate**

```csharp
public sealed class OrderAggregate : AggregateBase
{
    public string OrderId { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;

    protected override void RegisterEvents()
    {
        // Old event type (will be upcast to OrderCreatedEvent)
        Register<OrderCreatedEventV1>(v1 =>
        {
            OrderId = v1.OrderId;
            Amount = v1.Amount;
            Currency = "USD";
        });

        // New event type (v2)
        Register<OrderCreatedEvent>(oc =>
        {
            OrderId = oc.OrderId;
            Amount = oc.Amount;
            Currency = oc.Currency;
        });
    }
}
```

---

## Multi-Hop Upcasting Chains

Multi-hop chains (v1 → v2 → v3) are automatically applied during replay.

### Full Example: Three Event Versions

**Step 1: Define the events**

```csharp
// v1: OrderCreatedEventV1
[EventContract]
public sealed record OrderCreatedEventV1
{
    public string OrderId { get; set; } = default!;
    public decimal Amount { get; set; }
}

// v2: OrderCreatedEventV2 (added currency)
[EventContract]
public sealed record OrderCreatedEventV2
{
    public string OrderId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;

    public static int SchemaVersion => 2;
}

// v3: OrderCreatedEvent (added tax info)
[EventContract]
public sealed record OrderCreatedEvent
{
    public string OrderId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    public decimal TaxAmount { get; set; }

    public static int SchemaVersion => 3;
}
```

**Step 2: Define the upcasters**

```csharp
public sealed class OrderCreatedV1ToV2Upcaster
    : IEventUpcaster<OrderCreatedEventV1, OrderCreatedEventV2>
{
    public OrderCreatedEventV2 Upcast(OrderCreatedEventV1 source) =>
        new()
        {
            OrderId = source.OrderId,
            Amount = source.Amount,
            Currency = "USD",
        };
}

public sealed class OrderCreatedV2ToV3Upcaster
    : IEventUpcaster<OrderCreatedEventV2, OrderCreatedEvent>
{
    public OrderCreatedEvent Upcast(OrderCreatedEventV2 source) =>
        new()
        {
            OrderId = source.OrderId,
            Amount = source.Amount,
            Currency = source.Currency,
            TaxAmount = source.Amount * 0.1m,  // 10% tax on amount
        };
}
```

**Step 3: Register both upcasters**

```csharp
// Order matters: register from earliest to latest version
services.AddEventUpcaster<OrderCreatedEventV1, OrderCreatedEventV2, OrderCreatedV1ToV2Upcaster>();
services.AddEventUpcaster<OrderCreatedEventV2, OrderCreatedEvent, OrderCreatedV2ToV3Upcaster>();
```

**Step 4: Aggregate receives the final upcast event**

```csharp
public sealed class OrderAggregate : AggregateBase
{
    public string OrderId { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public decimal TaxAmount { get; private set; }

    protected override void RegisterEvents()
    {
        // The upcaster chain is applied before the aggregate applies the event.
        // Old v1 and v2 events arrive as OrderCreatedEvent (v3) after upcasting.
        Register<OrderCreatedEvent>(oc =>
        {
            OrderId = oc.OrderId;
            Amount = oc.Amount;
            Currency = oc.Currency;
            TaxAmount = oc.TaxAmount;
        });
    }
}
```

During replay:

- V1 events → upcast by `OrderCreatedV1ToV2Upcaster` → upcast by `OrderCreatedV2ToV3Upcaster` →
  arrive as `OrderCreatedEvent`
- V2 events → upcast by `OrderCreatedV2ToV3Upcaster` → arrive as `OrderCreatedEvent`
- V3 events → arrive as-is (no upcasting needed)

---

## Common Mistakes

### ❌ Mistake 1: Copying Metadata in the Upcaster

Metadata (`EventMetadata`: idempotency, correlation, user, timestamp, schema version) is carried by
the framework and re-attached by the store, so it should **not** be copied by the upcaster.

**Wrong:**

```csharp
public OrderCreatedEvent Upcast(OrderCreatedEventV1 source)
{
    return new()
    {
        Metadata = source.Metadata,  // Metadata is framework-managed; do not copy it
        OrderId = source.OrderId,
        Amount = source.Amount,
        Currency = "USD",
    };
}
```

**Correct:**

```csharp
public OrderCreatedEvent Upcast(OrderCreatedEventV1 source)
{
    return new()
    {
        OrderId = source.OrderId,
        Amount = source.Amount,
        Currency = "USD",
    };
}
```

The store attaches the source event's `EventMetadata` (idempotency, correlation, user) to the upcast
event automatically.

### ❌ Mistake 2: Creating a New Event Type Instead of Versioning

If the semantic meaning changes (e.g., "registration" → "registration with email verification"),
create a **new event type**, not a new version.

**Wrong (semantic change, not a versioning scenario):**

```csharp
[EventContract]
public sealed record UserRegisteredEvent
{
    public string Email { get; set; } = default!;

    public static int SchemaVersion => 2;
    public bool EmailVerified { get; set; }  // Required, breaking change
}
```

This conflates two different processes.

**Correct (introduce a new event type):**

```csharp
[EventContract]
public sealed record UserRegisteredEvent
{
    public string Email { get; set; } = default!;
}

[EventContract]
public sealed record UserRegisteredWithEmailVerificationEvent
{
    public string Email { get; set; } = default!;
    public bool EmailVerified { get; set; }
}
```

```csharp
protected override void RegisterEvents()
{
    Register<UserRegisteredEvent>(ur =>
    {
        Email = ur.Email;
        EmailVerified = false;
    });

    Register<UserRegisteredWithEmailVerificationEvent>(urwv =>
    {
        Email = urwv.Email;
        EmailVerified = urwv.EmailVerified;
    });
}
```

### ❌ Mistake 3: Forgetting a Safe Default for Legacy Values

When a breaking change adds a field, the upcaster must provide a deterministic default for old
events. Otherwise the aggregate applies a meaningless value.

**Wrong:**

```csharp
public OrderCreatedEvent Upcast(OrderCreatedEventV1 source)
{
    return new()
    {
        OrderId = source.OrderId,
        Amount = source.Amount,
        // Currency omitted — old events would hydrate Currency = null
    };
}
```

**Correct:**

```csharp
public OrderCreatedEvent Upcast(OrderCreatedEventV1 source)
{
    return new()
    {
        OrderId = source.OrderId,
        Amount = source.Amount,
        Currency = "USD",  // Deterministic default for legacy events
    };
}
```

### ❌ Mistake 4: Circular Upcaster Chains

The registry detects circular chains and throws an exception when it is constructed, but you can
prevent this by registering upcasters in order (v1 → v2 → v3).

**Wrong:**

```csharp
// This will throw at runtime
services.AddEventUpcaster<OrderCreatedEventV1, OrderCreatedEventV2, ...>();
services.AddEventUpcaster<OrderCreatedEventV2, OrderCreatedEventV1, ...>();  // Creates a cycle!
```

**Correct:**

```csharp
// Always register from earlier to later versions
services.AddEventUpcaster<OrderCreatedEventV1, OrderCreatedEventV2, ...>();
services.AddEventUpcaster<OrderCreatedEventV2, OrderCreatedEventV3, ...>();
```

---

## Testing Versioned Events

### Unit Test: Single-Hop Upcasting

```csharp
[Test]
public async Task Upcast_V1ToV2_PreservesDataAndDefaults()
{
    var upcaster = new OrderCreatedV1ToV2Upcaster();
    var v1Event = new OrderCreatedEventV1
    {
        OrderId = "123",
        Amount = 99.99m,
    };

    var v2Event = upcaster.Upcast(v1Event);

    await Assert.That(v2Event.OrderId).IsEqualTo("123");
    await Assert.That(v2Event.Amount).IsEqualTo(99.99m);
    await Assert.That(v2Event.Currency).IsEqualTo("USD");
}
```

### Integration Test: Replay with Upcasting

Legacy v1 rows are produced by an older deployment; the test seeds one directly in storage, then
loads the aggregate so the upcaster runs during replay.

```csharp
[Test]
public async Task Replay_WithV1Events_UpcastsToV2AndAppliesCorrectly()
{
    // 1. Register upcaster
    services.AddEventUpcaster<OrderCreatedEventV1, OrderCreatedEvent, OrderCreatedV1ToV2Upcaster>();

    // 2. Seed a V1 event row directly in storage (provider-specific), for example
    //    write an OrderCreatedEventV1 payload under the aggregate's stream.

    // 3. Load the aggregate (triggers replay with upcasting)
    var aggregate = await eventStore.GetAsync<OrderAggregate>("123", cancellationToken);

    // 4. Verify the aggregate state matches the upcast event
    await Assert.That(aggregate.OrderId).IsEqualTo("123");
    await Assert.That(aggregate.Amount).IsEqualTo(99.99m);
    await Assert.That(aggregate.Currency).IsEqualTo("USD");  // Upcast default
}
```

### Testing Unknown Events

```csharp
[Test]
public async Task Replay_WithUnknownEventType_ReturnsUnknownEventAndContinues()
{
    // 1. Seed an event whose persisted type name does not resolve to a registered event type

    // 2. Load the aggregate
    var aggregate = await eventStore.GetAsync<OrderAggregate>("123", cancellationToken);

    // 3. Verify replay continues without throwing
    await Assert.That(aggregate).IsNotNull();
    await Assert.That(aggregate.SkippedEvents).IsNotEmpty();

    // 4. In a real test, you'd have a mixture of known and unknown events
    //    to verify partial replay works correctly
}
```

---

## Summary

- **Additive changes (optional fields)** → No versioning needed
- **Breaking changes (required fields, removed fields, semantic changes)** → Increment SchemaVersion
- **Semantic meaning changes** → Create a new event type
- **Do not copy metadata in upcasters** — the store re-attaches `EventMetadata`
- **Register upcasters in order** (v1 → v2 → v3 → …)
- **Test multi-hop chains** and unknown event handling
- **Stream-backed providers apply upcasting during replay** (SQL Server, PostgreSQL, Azure Storage,
  MongoDB)

For more information, see [Event-Versioning-Strategy.md](Event-Versioning-Strategy.md).