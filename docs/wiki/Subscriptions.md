# Subscriptions

A subscription reads an aggregate's event stream from a recorded position and delivers each event to
a subscriber, including events written **before the subscriber existed**.

That is the difference from the two mechanisms that already carry events outwards:

| Mechanism | Carries | A consumer added today sees |
| --- | --- | --- |
| Change feed (`IAggregateChangeFeedProcessor`) | In-process callbacks around save and delete | Nothing historical; nothing after a restart |
| [Transactional outbox](Transactional-Outbox.md) | What a handler enqueued at write time | Nothing written before it was wired up |
| **Subscription** | The event stream itself | Everything, from the beginning |

So a projection rebuild, a new read model, or a late integration uses a subscription. Live
fan-out to another system uses the outbox.

## Registering one

```csharp
services.AddInMemoryEventStore();           // or any provider with an event stream
services.AddEventSubscription<Order, OrderProjection>();
```

```csharp
public sealed class OrderProjection(IReadModel readModel) : IEventSubscriber<Order>
{
    public string SubscriptionName => "order-projection";

    public async Task OnEventAsync(EventSubscriptionContext<Order> context, CancellationToken cancellationToken)
    {
        if (context.IsUnresolved)
            return;

        switch (context.Event)
        {
            case OrderPlaced placed:
                await readModel.AddAsync(context.AggregateId, placed.Total, cancellationToken);
                break;
        }
    }
}
```

`SubscriptionName` is the identity positions are recorded against. **Renaming a live subscription
abandons its progress** and replays every stream from the beginning.

## What is guaranteed

- **At-least-once delivery.** The position is written *after* the subscriber returns, so a crash
  between the two redelivers. Subscribers must be idempotent.
- **Version order within one aggregate.** Events for a given aggregate arrive in the order they were
  recorded.
- **No ordering between aggregates.** Streams are visited in an unspecified order. There is no
  global sequence to subscribe to — see
  [the design discussion](Subscriptions-And-Retention-Design.md#the-central-difficulty-there-is-no-global-order)
  for why, and what an external ordered log would add.
- **Tenant isolation.** Discovery, the event read and the position are all scoped to the current
  tenant. Two tenants running the same subscription name hold separate positions.

## Failure behaviour

If `OnEventAsync` throws, that aggregate's stream **stops at the failing event** and is retried from
there on the next cycle. Events already delivered stay delivered, other aggregates are unaffected,
and `SubscriptionCycleResult.StreamsFaulted` counts it.

This is deliberately fail-closed: skipping the event would lose data silently. The consequence is
that a permanently failing event **stalls that one stream indefinitely** — and every event behind it
— logging an error every cycle. Handling poison events inside the subscriber is still the first
answer. Dead-lettering is the escape hatch.

### Dead-lettering

Off by default, and it takes **two** deliberate steps to turn on, because an escape hatch that opens
too easily turns a visible stall into silent data loss:

```csharp
builder.Services.AddSingleton<ISubscriptionDeadLetterSink, MyDeadLetterSink>();
builder.Services.AddEventSubscription<Order, OrderProjection>(options => options.MaxDeliveryAttempts = 5);
```

`SubscriptionOptions.MaxDeliveryAttempts` defaults to `0`, which disables dead-lettering entirely,
and the default `ISubscriptionDeadLetterSink` accepts nothing and logs a warning when it refuses.
Setting either one alone changes nothing.

**The sink's return value decides whether the stream moves.** `DeadLetterAsync` returning `true`
means the failure is durably recorded, and only then does the checkpoint advance past the event.
Returning `false`, or throwing, leaves the stream stalled — which is correct, because the alternative
is losing the event with nothing anywhere to say it happened. A sink that throws is logged and
treated as a refusal; it does not bring the cycle down.

`DeadLetteredEvent` carries the subscription name, tenant scope, aggregate id, aggregate version,
event type, attempt count and the error text. It **identifies** the event rather than copying it: the
stream is unaltered and remains canonical, so everything needed to fetch and redeliver the event is
present, and no second copy of the payload is created that would outlive crypto-shredding.

Attempts are counted by `ISubscriptionDeliveryAttemptTracker`, a singleton because the processor is
scoped and rebuilt every cycle. The default counts **in memory**, so a restart gives a permanently
failing event another `MaxDeliveryAttempts` deliveries before it is dead-lettered again. That errs
toward retrying too often rather than abandoning too early; implement the interface over a store for
exact counts.

`SubscriptionCycleResult.EventsDeadLettered` reports how many events a cycle gave up on. It is zero
in a healthy deployment and worth alerting on.

An event whose type can no longer be resolved is *not* a failure: it is delivered with
`IsUnresolved` set to `true`, and the subscription advances past it. An application that has deleted
an event contract must not have every later event in the stream blocked by it.

## Stream discovery

The default is `EventStoreAggregateStreamDiscovery<T>`, over
`IEventStoreCore<T>.GetAggregateIdsAsync`. Every store implements that method and filters it by the
current tenant, so **discovery works on every provider that persists an event stream**, with no
snapshot store involved.

Deleted aggregates are included: a subscription that stopped seeing a stream the moment it was
deleted would never deliver the `Deleted` event, so a projection could not learn the aggregate had
gone.

It enumerates **every** stream of that aggregate type each cycle, which is the cost of having no
global sequence to read instead. Supply your own `IAggregateStreamDiscovery<T>` to narrow it — to
the aggregates changed since a watermark, say — by registering it before `AddEventSubscription`.

> An implementation must honour the current tenant. The event read is filtered too, so a leaky
> discovery will not deliver another tenant's *events* — but aggregate ids are themselves data, and
> that second filter masks the first in any test that only asserts on deliveries. This is why
> `SubscriptionTenancyTests` asserts on what discovery returns rather than on what arrives.

## Checkpoints

`ISubscriptionCheckpointStore` records the last delivered version per subscription, per tenant, per
aggregate. The tenant is part of `SubscriptionCheckpointKey` rather than something a store is
trusted to apply, so a position cannot be written without stating which tenant it belongs to.

The default registration is `InMemorySubscriptionCheckpointStore`, which **loses every position on
restart** and therefore replays all streams. That is fine for tests and local development; a
deployed application should register a durable store before `AddEventSubscription`:

```csharp
services.AddSqlServerEventStore();
services.AddSqlServerSubscriptionCheckpoints();   // or AddPostgresSubscriptionCheckpoints()
services.AddEventSubscription<Order, OrderProjection>();
```

| Provider | Durable checkpoints | Section |
| --- | --- | --- |
| SQL Server | `AddSqlServerSubscriptionCheckpoints()` | `EventStore:SqlServer:Subscriptions` |
| PostgreSQL | `AddPostgresSubscriptionCheckpoints()` | `EventStore:Postgres:Subscriptions` |
| Azure Storage | `AddAzureStorageSubscriptionCheckpoints()` | `EventStore:AzureStorage:Subscriptions` |
| MongoDB | `AddMongoDBSubscriptionCheckpoints()` | `EventStore:MongoDB:Subscriptions` |
| Cosmos DB | n/a — it has no event stream to subscribe to | |

Each creates its table or collection on first use and falls back to the event-store connection
string when none is given.

The guarantee that a position never moves backwards is expressed differently per store, because the
engines differ: SQL Server uses a guarded `UPDATE` then a conditional `INSERT`; PostgreSQL uses
`ON CONFLICT ... DO UPDATE ... WHERE`; MongoDB uses a filtered upsert and treats the resulting
duplicate-key error as "already further on"; Azure Table Storage has no conditional-on-value write
at all, so it reads and then writes conditionally on the ETag, retrying on contention
(`MaxWriteAttempts`, default 5). If Azure exhausts its retries it leaves the position behind, which
redelivers — safe, because delivery is at-least-once regardless.

The scope is stored in a single non-null `TenantId` column, with the system scope encoded as the
empty string by `TenantColumn`. That is shared rather than per-provider
because the encoding has to be identical everywhere, and because a nullable column would behave
differently between the two engines — PostgreSQL treats NULLs as distinct in a unique index, which
would silently allow duplicate system-scope rows.

A position never moves backwards: a cycle that re-read an earlier range cannot rewind a position
that is already further on, which would redeliver everything in between.

## Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `PollInterval` | 5 seconds | The delay between catch-up cycles |
| `CheckpointInterval` | 100 events | Write volume against redelivery after a crash. The position is also written whenever a stream is exhausted |

Bind from `EventStore:Subscriptions`, or configure inline:

```csharp
services.AddEventSubscription<Order, OrderProjection>(options => options.PollInterval = TimeSpan.FromSeconds(1));
```

## Retiring a subscription

A checkpoint row exists per subscription, per tenant, per aggregate, and nothing removed them
before — a retired subscription left one behind for every stream it had ever read, forever, on a
table that is otherwise only written to.

`ISubscriptionCheckpointMaintenance` is implemented by every checkpoint store, including the
in-memory one. Cast the store, or resolve it, and call it when a subscription is decommissioned:

```csharp
var removed = await maintenance.RemoveSubscriptionAsync("order-projection", TenantScope.For("acme"), ct);
await maintenance.RemoveCheckpointAsync(new("order-projection", scope, prunedAggregateId), ct);
```

Two properties worth knowing:

- **Removing a checkpoint is not neutral.** A subscription that is still running and loses its
  position restarts that stream from version 1 and redelivers every event in it. Delivery is
  at-least-once and subscribers must be idempotent, so that is a cost rather than a corruption — and
  it is why this is operator-invoked and nothing schedules it.
- **There is deliberately no wildcard.** Each call names one subscription *and* one `TenantScope`,
  matched exactly. `TenantScope.System` removes the system scope's own rows, not every tenant's;
  clearing every tenant means calling it per tenant. A "delete everything for this subscription"
  switch is how a deployment replays itself by accident.

## Limitations

- **No global ordering**, and no ordered live tail. Both are discussed in
  [the design page](Subscriptions-And-Retention-Design.md).
- **Durable checkpoints ship for every provider with an event stream** — SQL Server, PostgreSQL, Azure Storage and MongoDB. An InMemory deployment still replays every stream after a restart.
- **Cosmos DB cannot be subscribed to at all** — it persists no event stream, and `RunCycleAsync`
  throws `NotSupportedException`.
- **A permanently failing event stalls its stream by default.** Dead-lettering exists but is off
  until both `MaxDeliveryAttempts` and an `ISubscriptionDeadLetterSink` are configured. Delivery
  attempts are counted in memory, so they reset on restart, and the framework ships no durable sink —
  the sink is the application's.
- **Every cycle enumerates every stream** of the aggregate type. There is no global sequence to read
  instead, so this is linear in the number of aggregates rather than in the number of new events.
  Supply a narrower `IAggregateStreamDiscovery<T>` if that is too expensive.
- The in-memory event store keeps its data per instance and is registered transient, so a
  subscription over it must resolve the same instance — register it as a singleton.
- Subscriptions are not reported through `EventStoreCapabilities`. Whether one can run depends on
  the registered discovery implementation, which is dependency-injection state rather than a
  property of the store.

## Related

- [Transactional Outbox](Transactional-Outbox.md) — live fan-out, atomic with the save
- [Subscriptions and Retention — Design](Subscriptions-And-Retention-Design.md) — why it works this
  way, and what retention would add
- [Guarantees and Limitations](Guarantees-and-Limitations.md)
