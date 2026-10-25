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
that a permanently failing event **stalls that one stream indefinitely**, logging an error every
cycle. There is currently no dead-letter or skip mechanism — handle poison events inside the
subscriber, by catching and recording them yourself.

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

| Provider | Durable checkpoints |
| --- | --- |
| SQL Server | `AddSqlServerSubscriptionCheckpoints()` |
| PostgreSQL | `AddPostgresSubscriptionCheckpoints()` |
| Azure Storage, MongoDB, Cosmos DB | Not provided — supply an `ISubscriptionCheckpointStore` |

Both SQL stores create their table on first use (`AutoCreateTable`, on by default) and are
configured from `EventStore:SqlServer:Subscriptions` / `EventStore:Postgres:Subscriptions`. They
fall back to the event-store connection string when none is given.

The scope is stored in a single non-null `TenantId` column, with the system scope encoded as the
empty string by `SubscriptionCheckpointTenantColumn`. That is shared rather than per-provider
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

## Limitations

- **No global ordering**, and no ordered live tail. Both are discussed in
  [the design page](Subscriptions-And-Retention-Design.md).
- **Durable checkpoints ship for SQL Server and PostgreSQL only.** Azure Storage, MongoDB and an
  InMemory deployment replay every stream after a restart unless you supply a store.
- **Cosmos DB cannot be subscribed to at all** — it persists no event stream, and `RunCycleAsync`
  throws `NotSupportedException`.
- **A permanently failing event stalls its stream.** There is no dead-letter path.
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
