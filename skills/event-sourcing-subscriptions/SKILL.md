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

## Stream discovery is a seam, and it must be tenant-filtered

No provider holds an index of every stream, so discovery is supplied per provider:

- **InMemory** - automatic and exact.
- **SQL Server, PostgreSQL, MongoDB, Cosmos DB** - the queryable default, which needs a snapshot
  store and finds only **snapshotted** aggregates. An aggregate saved under a snapshot strategy that
  has not written one yet is invisible to the subscription.
- **Azure Storage** - none ships. Supply your own or subscriptions will not run.

Register yours before `AddEventSubscription` and it wins over the default.

**A discovery implementation that ignores the current tenant leaks.** The trap is that it will not
look like a leak: the event read is filtered too, so no cross-tenant *events* are delivered, and any
test that only asserts "the other tenant's subscriber received nothing" passes. Aggregate ids are
data. Assert on what discovery returns, not on what arrives - see `SubscriptionTenancyTests`, where
removing the discovery filter fails two tests and the acceptance scenarios stay green.

## Checkpoints are per tenant, structurally

The tenant is part of `SubscriptionCheckpointKey`, not something the store applies. A position
cannot be written without stating the tenant it belongs to.

If two tenants shared a position, whichever ran first would advance it past the other's unread
events and those events would **never** be delivered - a silent, permanent gap rather than a
visible error.

The default checkpoint store is in-memory and loses every position on restart, replaying all
streams. Register a durable one in anything deployed. No provider ships one yet.

## Failure stalls the stream, on purpose

If `OnEventAsync` throws, that stream stops at the failing event and retries from there next cycle.
Other aggregates continue, and `SubscriptionCycleResult.StreamsFaulted` counts it.

The consequence worth planning for: **a permanently failing event stalls that stream forever**, with
an error logged every cycle. There is no dead-letter path. Catch and record poison events inside the
subscriber if you need to move past them - the framework will not skip for you, because skipping
loses data silently.

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
