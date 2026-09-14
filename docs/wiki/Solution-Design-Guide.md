# Solution Design Guide

This guide helps application developers design an event-sourced solution before writing aggregate code. It is written for Purview EventSourcing projects that use `AggregateBase`, source-generated aggregate events, provider event stores, and optional queryable snapshots.

Use it in this order:

1. Model the business process on paper.
2. Choose aggregate boundaries and relationships.
3. Name aggregates, commands, events, and value objects using the repository rules.
4. Decide where validation belongs.
5. Sketch the event stream and read/query model.
6. Only then create aggregate code and tests.

For a printable template, use [Solution Design Worksheet](Solution-Design-Worksheet.md).

## Design Principles

- An aggregate is a consistency boundary, not a database table.
- An event is a fact that has happened, not an instruction to do something.
- Aggregate state is derived from its ordered event stream.
- Snapshots and query stores are optimizations/read models. The event stream remains the source of truth.
- Cross-aggregate workflows should be coordinated by services/process managers, not by loading other aggregates inside an aggregate method.
- Relational data belongs in query models, snapshots, projections, or referenced IDs, not as live joins inside aggregate invariants.
- Value objects carry reusable meaning and validation across aggregates.
- Validation should be explicit about when it runs: command-time, event creation, replay/hydration, save-time, or projection-time.
- Correlation IDs, idempotency markers, and transaction boundaries are part of the design, not just infrastructure details.

## Repository Rules To Design Against

### Aggregate Naming

Aggregate classes should end with `Aggregate`.

```csharp
public sealed partial class OrderAggregate : AggregateBase
{
}
```

`AggregateBase` derives the persisted aggregate type by trimming the `Aggregate` suffix and converting the remaining type name to lower kebab case:

| Class name | Aggregate type |
| --- | --- |
| `OrderAggregate` | `order` |
| `CustomerAggregate` | `customer` |
| `LearningHTMLTestAggregate` | `learning-html-test` |

The aggregate type is used by store implementations for stream grouping and lookup. Treat it as persisted data. Renaming an aggregate class or overriding the aggregate type after data exists is a migration decision.

The source generator supports aggregates that:

- are `partial`
- have no declared base class, where the generator adds `AggregateBase`
- directly inherit `AggregateBase`
- transitively inherit through a custom base class

If an aggregate uses a custom base class, confirm the chosen base class still inherits `AggregateBase` and does not hide event-sourcing behavior from developers. Aggregates with no declared base class get `AggregateBase` added by the generator.

### Event Naming

Generated event type names come from `[Event]` method names unless overridden with `EventName`.

Generated events normally end with `Event`. The event store name mapper trims that suffix and stores the event name as:

```text
{aggregate-type}.{event-name-without-event-suffix}
```

Example:

| Aggregate | Generated event type | Persisted event name |
| --- | --- | --- |
| `OrderAggregate` | `OrderCreatedEvent` | `order.order-created` |
| `CustomerAggregate` | `EmailChangedEvent` | `customer.email-changed` |
| `InventoryAggregate` | `StockReservedEvent` | `inventory.stock-reserved` |

Prefer past-tense event names:

- `OrderCreated`
- `CustomerRegistered`
- `EmailChanged`
- `StockReserved`
- `ReservationReleased`
- `OrderCancelled`

Avoid command-like event names:

- `CreateOrder`
- `ChangeEmail`
- `ReserveStock`
- `ValidateCustomer`

If the generated name is not the business language you want to persist, set it explicitly:

```csharp
[Event(EventName = "CustomerRegistered")]
public partial CustomerAggregate RegisterCustomer(string name, string email);
```

Use explicit `EventName` sparingly. It is useful for compatibility, integration contracts, or a domain term the generator cannot infer. Once persisted, event names are contracts.

### Event Namespace

By default, generated event classes are placed under:

```text
{AggregateNamespace}.{AggregateNameWithoutAggregateSuffix}Events
```

For example, `Purview.EventSourcing.Samples.Domain.OrderAggregate` generates events in an `OrderEvents` namespace. You can override the namespace at aggregate or method level with `EventNamespace`, but use that only when you need stable compatibility or a shared event namespace.

### Generated Method Shapes

Use generated methods for state changes that should become events:

```csharp
[Event]
public partial OrderAggregate CreateOrder(CustomerId customerId);
```

Keep a public wrapper when the business intent needs guard clauses, calculations, or multiple lower-level event methods:

```csharp
public OrderAggregate ConfirmOrder() => SetStatusCode(OrderStatusCode.Confirmed);

[Event]
private partial OrderAggregate SetStatusCode(OrderStatusCode status);
```

Use collection events for `EventStoreList<T>` and `EventStoreSet<T>` properties:

```csharp
public EventStoreSet<ProjectId> RelatedProjects { get; private set; } = [];

[CollectionEvent(nameof(RelatedProjects))]
public partial ReportUploadAggregate AddRelatedProject(ProjectId projectId);
```

Use `[Computed]` for deterministic values that callers must not supply directly and that generated hooks finalize before recording the event. Use `Manual = true` when generated property mapping is not expressive enough and you will write the `Apply(...)` method yourself.

## Paper-First Worksheet

Copy these tables into a design note or pull request before building a new feature.

### Business Capability

| Question | Answer |
| --- | --- |
| What business process is this? | |
| Who initiates it? | |
| What decisions must be consistent immediately? | |
| What can be eventually consistent? | |
| What external systems or UI screens need to know? | |
| What audit questions must be answerable later? | |

### Aggregate Candidates

| Candidate aggregate | Owns these decisions | Does not own | Lifecycle start | Lifecycle end |
| --- | --- | --- | --- | --- |
| | | | | |

Choose an aggregate when it owns rules that must be consistent in one event stream. Do not create one aggregate per relational table by default.

### Command And Event Sketch

| User/system intent | Aggregate method | Event fact | State changed | Validation needed |
| --- | --- | --- | --- | --- |
| Place order | `CreateOrder(...)` | `OrderCreated` | `CustomerId`, `Status` | Customer ID present |
| Add item | `AddLineItem(...)` | `LineItemAdded` or `LineItemsChanged` | `LineItems`, `TotalAmount` | Quantity, price, status |
| Confirm order | `ConfirmOrder()` | `OrderConfirmed` | `Status` | Has line items |

Keep method names intention-focused. Keep event names factual and past tense.

### Event Stream Sketch

| Version | Event | Important payload | Why this event exists |
| --- | --- | --- | --- |
| 1 | `OrderCreated` | `customerId` | Starts the order lifecycle |
| 2 | `LineItemAdded` | `productId`, `quantity`, `unitPrice` | Audits basket change |
| 3 | `OrderConfirmed` | `status` | Locks in the order |

Check that replaying the events in order recreates the aggregate state without calling external services.

### Relationship Sketch

| Relationship | Store on event/aggregate as | Enforce where | Query shape |
| --- | --- | --- | --- |
| Order belongs to customer | `CustomerId` value object/string | Application service or command validator | Projection joins customer snapshot |
| Order reserves inventory | `OrderId`, `ProductId`, `LocationId` | Workflow service across aggregates | Stock reservation read model |
| Report belongs to project | `ProjectId` value object | Command-time check or policy | Project report projection |

## Relational Data

Event-sourced aggregates should model relationships by identity, not by live object references.

Use this pattern inside aggregates:

```csharp
public CustomerId CustomerId { get; private set; }
public EventStoreList<OrderLineItem> LineItems { get; private set; } = new();
```

Avoid this inside aggregates:

```csharp
public CustomerAggregate Customer { get; private set; }
public List<InventoryAggregate> Inventory { get; private set; }
```

### When You Need Relational Views

Use query-side models for relational questions:

- customer profile with recent orders
- inventory by location and product
- order details with customer and shipment data
- audit pages across aggregate types

The project supports queryable snapshot stores for providers such as SQL Server, MongoDB, and Cosmos DB, and a null queryable store for core-only scenarios. Design relational views as projections/snapshots that can be rebuilt from event streams when possible.

When designing snapshot-backed SQL queries, distinguish between:

- provider-converted scalar value objects, which are ideal for invariants and serialization but may not support deep translation through `.Value`, and
- directly mapped complex snapshot members, which can support deep JSON-path predicates when the payload shape is explicitly supported and covered by integration tests.

If deep snapshot filtering is a hard requirement for a complex concept, model that query-facing shape deliberately instead of assuming a scalar wrapper will remain queryable.

The current repository also includes an in-memory provider and quick-start sample. Treat in-memory storage as a development/testing convenience unless a production use case has explicitly accepted its durability limits.

### Cross-Aggregate Rules

If a rule needs more than one aggregate, do not hide that rule inside one aggregate.

Use an application service or process manager:

```csharp
public sealed class CartCheckoutService(IEventStore eventStore)
{
    public async Task CheckoutAsync(string customerId, CartItem[] items, CancellationToken cancellationToken)
    {
        var order = await eventStore.CreateAsync<OrderAggregate>(
            aggregateId: Guid.NewGuid().ToString(),
            cancellationToken: cancellationToken);

        order.CreateOrder(customerId);

        foreach (var item in items)
            order.AddLineItem(item.ProductId, item.ProductName, item.Quantity, item.UnitPrice);

        await eventStore.SaveAsync(order, cancellationToken);
    }
}
```

If several aggregates must be saved together, use the transaction support provided by the selected store where available. Still design each aggregate as if it can replay independently.

`EventStoreTransaction` chooses the strongest compatible coordinator available:

- If all enlisted stores share a native transaction boundary, commits are atomic.
- If no shared native boundary exists, commits are sequential under a shared correlation ID.
- Sequential fallback does not roll back aggregates that were already persisted.

Design cross-aggregate workflows with this distinction in mind. For mixed stores or unsupported transaction boundaries, use idempotent commands, compensating events, and retry-safe process managers.

## Value Objects

Use value objects for concepts that are more meaningful than primitive strings, integers, or decimals:

- `EmailAddress`
- `Name`
- `CustomerId`
- `ProjectId`
- `Money`
- `OrderStatus`
- `CurrencyCode`

Value objects are a good place for normalization and validation that should apply everywhere.

```csharp
[Scalar]
public readonly partial record struct EmailAddress
{
    public string Value { get; }

    static partial void OnNormalize(ref string value) =>
        value = value.Trim().ToLowerInvariant();

    static partial void OnValidate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Email address cannot be empty.", nameof(value));
    }
}
```

Use scalar value objects when one primitive value carries the meaning. Use full value objects when the concept has multiple fields, such as `Money` with `Amount` and `Currency`.

Generated value objects distinguish strict creation from hydration:

- `Create(...)` normalizes and validates command-time input.
- `Hydrate(...)` rebuilds persisted state and should be replay-safe.
- `[Scalar]` and `[ValueObject]` default to hydration-oriented deserialization.
- `GenerateEmpty`, implicit primitive conversion, comparison operators, JSON converters, and constructor generation are configurable.

### Contextual Value Objects

Use contextual value objects when validity depends on the current aggregate state. The sample `OrderStatus` validates allowed transitions against the current `OrderAggregate`.

This is useful for:

- state machines
- date ranges relative to aggregate state
- limits that depend on current totals
- transitions that must not be checked again during replay

Design rule: command-time creation can be strict; replay/hydration must be able to rebuild historical state.

## Validation Across The Board

Use the smallest validation scope that correctly owns the rule.

| Rule type | Best location | Runs during replay? | Example |
| --- | --- | --- | --- |
| Primitive shape | Value object | Usually hydrate-safe | Email format, non-empty name |
| Command input | Public aggregate method | No | Quantity must be positive |
| Property normalization | `On<Property>Changing` hook | No | Trim/lowercase email |
| State transition | Contextual value object or aggregate method | No for strict command path | Draft to Confirmed only |
| State mutation | Generated/manual `Apply(...)` | Yes | Set `Status`, update totals |
| Whole aggregate validity | DataAnnotations default validator or FluentValidation `IValidator<TAggregate>` at save | Save-time | Snapshot must be internally valid |
| Cross-aggregate rule | Application service/process manager | No | Customer must exist before order |
| Read model rule | Projection/query model | Projection-time | Denormalized search fields |

### Aggregate Method Guards

Public aggregate methods should protect business intent before raising events:

```csharp
public InventoryAggregate ReserveStock(int quantity, string? orderId)
{
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

    if (quantity > AvailableQuantity)
        throw new InvalidOperationException(
            $"Cannot reserve {quantity} units. Only {AvailableQuantity} available.");

    return ReserveStock(
        quantityOnHand: QuantityOnHand,
        reservedQuantity: ReservedQuantity + quantity,
        orderId);
}
```

### Generator Hooks

Use generated hooks for local normalization and event-specific extension points:

- `On<Property>Changing(ref value)` runs before event creation on the command path.
- `On<Property>Changed(previous, current)` runs in `Apply(...)`, including replay.
- `OnRaising<EventName>Event(ref ...)` runs before the generated event is recorded.
- `OnRaised<EventName>Event(@event)` runs after event creation.
- `OnApplied<EventName>Event(@event)` runs after application.
- `OnShouldApply<EventName>Event(@event, ref bool shouldApply)` can skip generated application.

Because `On<Property>Changed` runs during replay, keep it deterministic and free of external side effects.

### Save-Time Validation

Stores run aggregate validation before persistence. With no custom validator, the current implementation uses `DefaultAggregateValidator<TAggregate>`, which validates standard DataAnnotations such as `[Range]`. Store constructors accept `IAggregateValidator<TAggregate>?` — when null, the default DataAnnotations validator is used.

FluentValidation integration is available in the separate `Purview.EventSourcing.FluentValidation` package, which provides `FluentValidationAggregateValidator<TAggregate>` to adapt `FluentValidation.IValidator<T>` to `IAggregateValidator<T>`. Register it via `AddFluentValidationAdapter<TAggregate, TValidator>()` or `AddFluentValidationAdapter<TAggregate>()` DI extensions.

Use save-time validation for aggregate-wide consistency checks that should pass before persistence:

```csharp
public sealed class OrderAggregate : AggregateBase
{
    [Range(0, double.MaxValue)]
    public decimal TotalAmount { get; private set; }
}
```

Do not rely only on save-time validation for user-facing command errors. Put business guard clauses near the command method as well so invalid operations fail before an event is created.

`SaveResult<TAggregate>` carries `Saved`, `Skipped`, and `ValidationResult`. Check `IsValid` or call `EnsureValid()` when callers need validation failures surfaced as exceptions.

## Implementation Pattern

Prefer this aggregate shape:

```csharp
[Aggregate]
public sealed partial class OrderAggregate : AggregateBase
{
    public CustomerId CustomerId { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;
    public EventStoreList<OrderLineItem> LineItems { get; private set; } = new();
    public decimal TotalAmount { get; private set; }

    public OrderAggregate ConfirmOrder() =>
        SetStatusCode(OrderStatusCode.Confirmed);

    public OrderAggregate AddLineItem(string productId, string productName, int quantity, decimal unitPrice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfNegative(unitPrice);

        var updated = LineItems.Append(new OrderLineItem(productId, productName, quantity, unitPrice)).ToList();

        return AddLineItem(
            new EventStoreList<OrderLineItem>(updated),
            totalAmount: updated.Sum(m => m.Quantity * m.UnitPrice));
    }

    [Event(EventName = "OrderCreated")]
    public partial OrderAggregate CreateOrder(CustomerId customerId);

    [Event(EventName = "OrderLineItemsChanged")]
    private partial OrderAggregate AddLineItem(EventStoreList<OrderLineItem> lineItems, decimal totalAmount);

    [Event(EventName = "OrderStatusChanged")]
    private partial OrderAggregate SetStatusCode(OrderStatusCode status);
}
```

The pattern is:

1. Public methods express business intent and validate inputs.
2. Private generated methods record the factual state change when callers should not raise it directly.
3. Events carry enough data to replay state.
4. Value objects normalize and validate reusable concepts.
5. Services coordinate multiple aggregates.

When a state change should only be recorded if a property actually changes, a manual aggregate can use `CompareRecordAndApply(...)`; generated aggregate methods already provide the higher-level convention for most cases.

## Event Payload Design

Put enough information on the event to replay the aggregate without external reads.

Good payloads:

- stable IDs
- normalized value objects
- values needed to update aggregate state
- metadata that explains the operation, such as `orderId` on stock reservations
- timestamps when the business time differs from event commit time

Be careful with:

- personally identifiable information
- large documents or binary payloads
- fields copied from another aggregate that may become stale
- values that can be calculated deterministically from event payload

For metadata parameters that should be stored on generated events but not mapped to aggregate properties, use `[Metadata]`.

```csharp
[Event]
public partial InventoryAggregate ReserveStock(
    int quantityOnHand,
    int reservedQuantity,
    [Metadata] string? orderId);
```

Use `[Property(nameof(Property))]` when the parameter name does not match the aggregate property.

```csharp
[Event]
public partial InventoryAggregate Create(
    string productId,
    [Property(nameof(QuantityOnHand))] int initialQuantity = 0);
```

## Schema Evolution

Events are persisted facts. Changing them is a compatibility decision.

- Add optional event properties when possible.
- Avoid changing the meaning of an existing property.
- Use `[Event(Version = N)]` for breaking schema versions.
- Add upcasters when old events need to hydrate into newer event shapes.
- Do not change generated event/aggregate naming conventions after data exists unless you plan a migration. (`EventSuffixLength` is a provider option that controls the zero-padded numeric suffix on event row IDs, not a naming convention.)
- Treat aggregate type names and event names as persisted contracts.

## Operation Semantics

Document these choices for each workflow:

| Concern | Design decision |
| --- | --- |
| Aggregate ID | Who creates it: caller, `IAggregateIdFactory`, or store default? |
| Correlation ID | How is it propagated across service calls and transactions? |
| Idempotency | Should `UseIdempotencyMarker` be enabled for retries? |
| Principal ID | Will saves require a `ClaimsPrincipal` identifier? |
| Delete behavior | Soft delete, restore, or permanent delete? |
| Snapshot behavior | Use snapshots, skip snapshots for replay, or apply operation-specific snapshot strategy? |
| Notifications | Which change feed notifications should fire? |

These details affect reliability and auditability as much as aggregate code does.

## Testing Checklist

For each aggregate:

- Can version 1 of the stream be created with one clear lifecycle-start event?
- Does every public method either raise an event or intentionally no-op?
- Do invalid commands fail before an event is recorded?
- Does replaying the event stream rebuild the same state?
- Are value object rules tested independently?
- Are state-machine transitions tested for allowed and rejected paths?
- Are cross-aggregate workflows tested at service level?
- Are query/projection shapes tested separately from aggregate behavior?

## Design Review Checklist

Before coding, reviewers should be able to answer yes to these:

- Aggregate names end in `Aggregate` and produce stable aggregate type names.
- Event names are past-tense business facts.
- Event payloads can replay state without external services.
- Relationships use IDs/value objects inside events and aggregates.
- Relational screens are designed as query models, snapshots, or projections.
- Validation is assigned to the right layer.
- Replay-safe code has no external side effects.
- Schema evolution and event versioning have been considered.
- Tests cover command guards, replay, value objects, and workflows.
