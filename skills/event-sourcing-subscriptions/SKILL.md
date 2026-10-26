---
name: event-sourcing-subscriptions
description: Build projections, read models and integrations that read a Purview.EventSourcing event stream from a recorded position, including events written before the consumer existed. Use when adding a subscriber, a projection rebuild, stream discovery, or a checkpoint store, or when choosing between a subscription, the change feed and the transactional outbox.
license: MIT
metadata:
  package: Purview.EventSourcing
  category: event-sourcing
---

# Subscriptions

A subscription reads the event stream itself, so it delivers events written before the subscriber
existed. That is the one thing the change feed and the outbox cannot do.

## Pick the right mechanism first

| You want | Use |
| -- | -- |
| A projection or read model that can be rebuilt from history | **Subscription** |
| A consumer added after the system was already running | **Subscription** |
| To publish to another system as part of the write | **Outbox** - atomic with the save |
| To react in-process during save or delete | **Change feed** |

Reaching for the outbox to build a projection is the common mistake. It only contains what a handler
enqueued at write time, so the projection can never be rebuilt and a replay is impossible.

## Write the subscriber idempotently

Delivery is at-least-once: the position is written *after* `OnEventAsync` returns, so a crash
between the two redelivers. This is not a rare edge case - it happens on every ungraceful shutdown.

```csharp
public sealed class OrderProjection(IReadModel readModel) : IEventSubscriber<Order>
{
    public string SubscriptionName => "order-projection";

    public async Task OnEventAsync(EventSubscriptionContext<Order> context, CancellationToken cancellationToken)
    {
        if (context.IsUnresolved)
            return;

        // Upsert keyed by (aggregate id, version), not an append - redelivery must be harmless.
        if (context.Event is OrderPlaced placed)
            await readModel.UpsertAsync(context.AggregateId, context.AggregateVersion, placed.Total, cancellationToken);
    }
}
```

**Do not rename a live `SubscriptionName`.** Positions are recorded against it, so a rename
abandons all progress and replays every stream from the beginning. If that is what you want, say so
deliberately.

## Ordering: what you actually get

- **Ordered within one aggregate.** Guaranteed.
- **Unordered between aggregates.** There is no global sequence. Streams are per-aggregate on every
  provider, and Azure Table Storage cannot hold a cross-partition sequence at all.

So a projection keyed by aggregate id is safe. A projection that depends on the relative order of
two *different* aggregates' events is not, and no option will make it so. Model it as a single
aggregate instead, or accept eventual convergence.

## Stream discovery, and why a custom one is usually a mistake

The default reads `IEventStoreCore<T>.GetAggregateIdsAsync`, which every store implements and
filters by the current tenant. It works on every provider with an event stream - no snapshot store,
no provider-specific wiring.

The cost: **every cycle enumerates every stream** of that aggregate type, so the work is linear in
the number of aggregates, not in the number of new events. With no global sequence there is nothing
cheaper to read. If that is too expensive, register your own `IAggregateStreamDiscovery<T>` before
`AddEventSubscription` to narrow it - aggregates changed since a watermark, for instance.

**If you do write one, it must honour the current tenant.** The trap is that a leak will not look
like one: the event read is filtered too, so no cross-tenant *events* are delivered, and any test
that only asserts "the other tenant's subscriber received nothing" passes. Aggregate ids are data.
Assert on what discovery returns, not on what arrives - in `SubscriptionTenancyTests`, removing the
filter fails two tests while every acceptance scenario stays green.

## Checkpoints are per tenant, structurally

The tenant is part of `SubscriptionCheckpointKey`, not something the store applies. A position
cannot be written without stating the tenant it belongs to.

If two tenants shared a position, whichever ran first would advance it past the other's unread
events and those events would **never** be delivered - a silent, permanent gap rather than a
visible error.

The default checkpoint store is in-memory and loses every position on restart, replaying all
streams. Register a durable one in anything deployed, **before** `AddEventSubscription`:

```csharp
services.AddSqlServerSubscriptionCheckpoints();   // or AddPostgresSubscriptionCheckpoints()
services.AddEventSubscription<Order, OrderProjection>();
```

Every provider with an event stream ships one - `AddSqlServerSubscriptionCheckpoints`,
`AddPostgresSubscriptionCheckpoints`, `AddAzureStorageSubscriptionCheckpoints`,
`AddMongoDBSubscriptionCheckpoints`. Cosmos DB has no event stream, so there is nothing to
checkpoint.

If you write your own, encode the scope with `TenantColumn` rather than
inventing your own scheme, and note two traps the shipped stores had to handle:

- **Do not use a nullable tenant column.** PostgreSQL treats NULLs as distinct in a unique index, so
  the system scope would silently accumulate duplicate rows.
- **Do not build the key by concatenating with a delimiter** unless you encode the segments. A
  subscription name, tenant or aggregate id containing the delimiter makes two different keys
  identical. The Azure store Base64Url-encodes each segment; the Mongo store uses a compound `_id`
  document, which is unambiguous by construction.

The position must never move backwards: a cycle that re-read an earlier range would otherwise rewind
a position that is already further on and redeliver everything in between.

## Failure stalls the stream, on purpose

If `OnEventAsync` throws, that stream stops at the failing event and retries from there next cycle.
Other aggregates continue, and `SubscriptionCycleResult.StreamsFaulted` counts it.

The consequence worth planning for: **a permanently failing event stalls that stream forever**, and
every event behind it with it. Handling poison events inside the subscriber is still the first
answer. The escape hatch is dead-lettering.

### Dead-lettering needs two deliberate steps

Neither is a default, because an escape hatch that opens too easily turns a visible stall into
silent data loss:

```csharp
builder.Services.AddSingleton<ISubscriptionDeadLetterSink, MyDeadLetterSink>();
builder.Services.AddEventSubscription<Order, OrderProjection>(options => options.MaxDeliveryAttempts = 5);
```

`MaxDeliveryAttempts` is `0` by default, and the default sink accepts nothing. Set one without the
other and behaviour is unchanged.

**The sink's return value is the contract.** The checkpoint advances past the event only when
`DeadLetterAsync` returns `true`. Return `false` - or throw - and the stream stays stalled, which is
correct: the alternative is losing the event with nothing anywhere recording that it happened. Do
not return `true` from a sink that failed to write.

```csharp
public async Task<bool> DeadLetterAsync(DeadLetteredEvent deadLettered, CancellationToken ct)
{
    try { await _store.RecordAsync(deadLettered, ct); return true; }
    catch (Exception ex) { _logger.LogError(ex, "..."); return false; }   // stall, don't skip
}
```

`DeadLetteredEvent` **identifies** the event - subscription, tenant scope, aggregate id, version,
event type, attempts, error - and does not carry the payload. The stream is still the canonical
record and is not altered, so everything needed to fetch and redeliver it is there. A sink that
copied the payload would become a second, divergent copy that outlives crypto-shredding.

**Attempts are counted per process, not for all time.** The tracker is in memory, so a restart gives
a permanently failing event another `MaxDeliveryAttempts` deliveries. That errs toward retrying too
often rather than abandoning too early; implement `ISubscriptionDeliveryAttemptTracker` over your own
store if you need exact counts.

`SubscriptionCycleResult.EventsDeadLettered` is worth alerting on. It is zero in a healthy
deployment, and non-zero means events were not handled and something has to go and look at them.

An unresolvable event type is not a failure. It arrives with `IsUnresolved` set and the subscription
advances past it, so deleting an event contract does not block the rest of the stream.

## Gotchas

- **Cosmos DB cannot be subscribed to.** It persists no event stream; `RunCycleAsync` throws
  `NotSupportedException`.
- **The in-memory store is transient and keeps data per instance.** A subscription over it reads a
  different, empty store unless you register it as a singleton.
- Subscriptions are **not** reported through `EventStoreCapabilities` - whether one can run depends
  on the registered discovery, which is DI state rather than a store property.

See `docs/wiki/Subscriptions.md` for the full reference and
`docs/wiki/Subscriptions-And-Retention-Design.md` for why the ordering model is what it is.
