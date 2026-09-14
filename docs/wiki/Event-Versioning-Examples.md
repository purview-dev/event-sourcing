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

When you add a new optional field to an event, no versioning is needed. Old events will deserialize successfully with the new field set to its default value.

### Example: Adding an Optional Phone Number

**Initial event (v1, implicit SchemaVersion = 1):**
```csharp
public sealed class CustomerRegistered : EventBase
{
    public string CustomerId { get; set; } = default!;
    public string Email { get; set; } = default!;
    
    protected override void BuildEventHash(ref HashCode hash)
    {
        hash.Add(CustomerId);
        hash.Add(Email);
    }
}
```

**After adding an optional field (still v1, no SchemaVersion bump needed):**
```csharp
public sealed class CustomerRegistered : EventBase
{
    public string CustomerId { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? PhoneNumber { get; set; }  // Optional, new field
    
    protected override void BuildEventHash(ref HashCode hash)
    {
        hash.Add(CustomerId);
        hash.Add(Email);
        // Note: Don't hash optional fields that might be null
    }
}
```

**Aggregate apply logic:**
```csharp
protected override void RegisterEvents()
{
    Register<CustomerRegistered>(cr =>
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

When you make a **breaking change** to an event's payload (required field added, meaning changed, property removed), bump the `SchemaVersion`.

### Example: Making Phone Number Required

**Old event (v1):**
```csharp
public sealed class CustomerRegistered : EventBase
{
    public string CustomerId { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? PhoneNumber { get; set; }  // Was optional
    
    protected override void BuildEventHash(ref HashCode hash)
    {
        hash.Add(CustomerId);
        hash.Add(Email);
    }
}
```

**New event (v2, breaking change):**
```csharp
public sealed class CustomerRegistered : EventBase
{
    public string CustomerId { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string PhoneNumber { get; set; } = default!;  // Now required
    
    public override int SchemaVersion => 2;
    
    protected override void BuildEventHash(ref HashCode hash)
    {
        hash.Add(CustomerId);
        hash.Add(Email);
        hash.Add(PhoneNumber);  // Now included in hash
    }
}
```

### Defining the Upcaster

```csharp
public sealed class CustomerRegisteredV1ToV2Upcaster
    : IEventUpcaster<CustomerRegistered, CustomerRegistered>
{
    public CustomerRegistered Upcast(CustomerRegistered source)
    {
        return new()
        {
            Details = source.Details,  // Always preserve metadata
            CustomerId = source.CustomerId,
            Email = source.Email,
            PhoneNumber = source.PhoneNumber ?? "UNKNOWN",  // Default for old events
        };
    }
}
```

---

## Single-Hop Upcasting

Single-hop upcasting converts v1 events directly to v2 during replay.

### Full Example: Order Events

**Step 1: Define the events**

```csharp
// Order event v1 (no currency)
public sealed class OrderCreatedV1 : EventBase
{
    public string OrderId { get; set; } = default!;
    public decimal Amount { get; set; }
    
    protected override void BuildEventHash(ref HashCode hash)
    {
        hash.Add(OrderId);
        hash.Add(Amount);
    }
}

// Order event v2 (with currency, breaking change)
public sealed class OrderCreated : EventBase
{
    public string OrderId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    
    public override int SchemaVersion => 2;
    
    protected override void BuildEventHash(ref HashCode hash)
    {
        hash.Add(OrderId);
        hash.Add(Amount);
        hash.Add(Currency);
    }
}
```

**Step 2: Define the upcaster**

```csharp
public sealed class OrderCreatedV1ToV2Upcaster
    : IEventUpcaster<OrderCreatedV1, OrderCreated>
{
    public OrderCreated Upcast(OrderCreatedV1 source)
    {
        return new()
        {
            Details = source.Details,
            OrderId = source.OrderId,
            Amount = source.Amount,
            Currency = "USD",  // Default currency for old events
        };
    }
}
```

**Step 3: Register the upcaster in DI**

```csharp
services.AddEventUpcaster<OrderCreatedV1, OrderCreated, OrderCreatedV1ToV2Upcaster>();
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
        // Old event type (will be upcast to OrderCreated)
        Register<OrderCreatedV1>(v1 =>
        {
            OrderId = v1.OrderId;
            Amount = v1.Amount;
            Currency = "USD";
        });

        // New event type (v2)
        Register<OrderCreated>(oc =>
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
// v1: OrderCreatedV1
public sealed class OrderCreatedV1 : EventBase
{
    public string OrderId { get; set; } = default!;
    public decimal Amount { get; set; }
    
    protected override void BuildEventHash(ref HashCode hash)
    {
        hash.Add(OrderId);
        hash.Add(Amount);
    }
}

// v2: OrderCreatedV2 (added currency)
public sealed class OrderCreatedV2 : EventBase
{
    public string OrderId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    
    public override int SchemaVersion => 2;
    
    protected override void BuildEventHash(ref HashCode hash)
    {
        hash.Add(OrderId);
        hash.Add(Amount);
        hash.Add(Currency);
    }
}

// v3: OrderCreated (added tax info)
public sealed class OrderCreated : EventBase
{
    public string OrderId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    public decimal TaxAmount { get; set; }
    
    public override int SchemaVersion => 3;
    
    protected override void BuildEventHash(ref HashCode hash)
    {
        hash.Add(OrderId);
        hash.Add(Amount);
        hash.Add(Currency);
        hash.Add(TaxAmount);
    }
}
```

**Step 2: Define the upcasters**

```csharp
public sealed class OrderCreatedV1ToV2Upcaster
    : IEventUpcaster<OrderCreatedV1, OrderCreatedV2>
{
    public OrderCreatedV2 Upcast(OrderCreatedV1 source)
    {
        return new()
        {
            Details = source.Details,
            OrderId = source.OrderId,
            Amount = source.Amount,
            Currency = "USD",
        };
    }
}

public sealed class OrderCreatedV2ToV3Upcaster
    : IEventUpcaster<OrderCreatedV2, OrderCreated>
{
    public OrderCreated Upcast(OrderCreatedV2 source)
    {
        return new()
        {
            Details = source.Details,
            OrderId = source.OrderId,
            Amount = source.Amount,
            Currency = source.Currency,
            TaxAmount = source.Amount * 0.1m,  // 10% tax on amount
        };
    }
}
```

**Step 3: Register both upcasters**

```csharp
// Order matters: register from earliest to latest version
services.AddEventUpcaster<OrderCreatedV1, OrderCreatedV2, OrderCreatedV1ToV2Upcaster>();
services.AddEventUpcaster<OrderCreatedV2, OrderCreated, OrderCreatedV2ToV3Upcaster>();
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
        // Old v1 and v2 events arrive as OrderCreated (v3) after upcasting.
        Register<OrderCreated>(oc =>
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
- V1 events → upcast by OrderCreatedV1ToV2Upcaster → upcast by OrderCreatedV2ToV3Upcaster → arrive as OrderCreated
- V2 events → upcast by OrderCreatedV2ToV3Upcaster → arrive as OrderCreated
- V3 events → arrive as-is (no upcasting needed)

---

## Common Mistakes

### ❌ Mistake 1: Forgetting to Copy EventDetails

**Wrong:**
```csharp
public OrderCreated Upcast(OrderCreatedV1 source)
{
    return new()
    {
        // Forgot to copy Details!
        OrderId = source.OrderId,
        Amount = source.Amount,
        Currency = "USD",
    };
}
```

This breaks idempotency, correlation, and audit trails.

**Correct:**
```csharp
public OrderCreated Upcast(OrderCreatedV1 source)
{
    return new()
    {
        Details = source.Details,  // Always copy
        OrderId = source.OrderId,
        Amount = source.Amount,
        Currency = "USD",
    };
}
```

### ❌ Mistake 2: Creating a New Event Type Instead of Versioning

If the semantic meaning changes (e.g., "registration" → "registration with email verification"), create a **new event type**, not a new version.

**Wrong (semantic change, not a versioning scenario):**
```csharp
public sealed class UserRegistered : EventBase
{
    // v1: just email
    public string Email { get; set; } = default!;
    
    // v2: now requires email verification
    public string Email { get; set; } = default!;
    public bool EmailVerified { get; set; }  // Required, breaking change
    
    public override int SchemaVersion => 2;
}
```

This conflates two different processes.

**Correct (introduce a new event type):**
```csharp
public sealed class UserRegistered : EventBase
{
    public string Email { get; set; } = default!;
}

public sealed class UserRegisteredWithEmailVerification : EventBase
{
    public string Email { get; set; } = default!;
    public bool EmailVerified { get; set; }
}

// Use in the aggregate:
protected override void RegisterEvents()
{
    Register<UserRegistered>(ur =>
    {
        Email = ur.Email;
        EmailVerified = false;
    });

    Register<UserRegisteredWithEmailVerification>(urwv =>
    {
        Email = urwv.Email;
        EmailVerified = urwv.EmailVerified;
    });
}
```

### ❌ Mistake 3: Not Hashing All Required Fields in SchemaVersion >= 2

When you increment SchemaVersion, all non-optional fields must be included in the hash.

**Wrong:**
```csharp
public override int SchemaVersion => 2;

protected override void BuildEventHash(ref HashCode hash)
{
    hash.Add(OrderId);
    // Forgot to hash Currency even though it's now required
}
```

This creates hash collisions and violates the event's integrity.

**Correct:**
```csharp
public override int SchemaVersion => 2;

protected override void BuildEventHash(ref HashCode hash)
{
    hash.Add(OrderId);
    hash.Add(Amount);
    hash.Add(Currency);  // Include all required fields
}
```

### ❌ Mistake 4: Circular Upcaster Chains

The registry detects circular chains and throws an exception at registration time, but you can prevent this by registering upcasters in order (v1 → v2 → v3).

**Wrong:**
```csharp
// This will throw at runtime
services.AddEventUpcaster<OrderCreatedV1, OrderCreatedV2, ...>();
services.AddEventUpcaster<OrderCreatedV2, OrderCreatedV1, ...>();  // Creates a cycle!
```

**Correct:**
```csharp
// Always register from earlier to later versions
services.AddEventUpcaster<OrderCreatedV1, OrderCreatedV2, ...>();
services.AddEventUpcaster<OrderCreatedV2, OrderCreatedV3, ...>();
```

---

## Testing Versioned Events

### Unit Test: Single-Hop Upcasting

```csharp
[Test]
public async Task Upcast_V1ToV2_PreservesDataAndDefaults()
{
    var upcaster = new OrderCreatedV1ToV2Upcaster();
    var v1Event = new OrderCreatedV1
    {
        OrderId = "123",
        Amount = 99.99m,
        Details = { IdempotencyId = "idempotency-123" },
    };

    var v2Event = upcaster.Upcast(v1Event);

    await Assert.That(v2Event.OrderId).IsEqualTo("123");
    await Assert.That(v2Event.Amount).IsEqualTo(99.99m);
    await Assert.That(v2Event.Currency).IsEqualTo("USD");
    await Assert.That(v2Event.Details.IdempotencyId).IsEqualTo("idempotency-123");
}
```

### Integration Test: Replay with Upcasting

```csharp
[Test]
public async Task Replay_WithV1Events_UpcastsToV2AndAppliesCorrectly()
{
    // 1. Register upcaster
    services.AddEventUpcaster<OrderCreatedV1, OrderCreated, ...>();

    // 2. Save a V1 event directly to storage
    var v1Event = new OrderCreatedV1 { OrderId = "123", Amount = 99.99m };
    await eventStore.SaveAsync(aggregateId, [v1Event], ...);

    // 3. Load the aggregate (triggers replay with upcasting)
    var aggregate = await eventStore.GetAsync(aggregateId);

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
    // 1. Save an event with a type that doesn't exist
    var unknownEvent = new CustomEvent { ... };

    // 2. Load the aggregate
    var aggregate = await eventStore.GetAsync(aggregateId);

    // 3. Verify replay continues without throwing
    await Assert.That(aggregate).IsNotNull();

    // 4. In a real test, you'd have a mixture of known and unknown events
    // to verify partial replay works correctly
}
```

---

## Summary

- **Additive changes (optional fields)** → No versioning needed
- **Breaking changes (required fields, removed fields, semantic changes)** → Increment SchemaVersion
- **Semantic meaning changes** → Create a new event type
- **Always copy EventDetails** in upcasters
- **Register upcasters in order** (v1 → v2 → v3 → …)
- **Test multi-hop chains** and unknown event handling
- **All providers apply upcasting during replay** (SQL Server, Azure Storage, MongoDB)

For more information, see [Event-Versioning-Strategy.md](Event-Versioning-Strategy.md).
