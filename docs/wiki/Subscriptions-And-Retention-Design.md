# Subscriptions and Retention — Design

> **Status: proposal. Nothing on this page is implemented.**
>
> No type, option or method described here exists in the framework today. This page is a design to
> be agreed before any code is written; treat every signature as illustrative. When the work lands,
> this page is replaced by documentation of what was actually built, and
> [Provider Feature Matrix](Provider-Feature-Matrix.md) and
> [Guarantees and Limitations](Guarantees-and-Limitations.md) are updated with it.

## What already exists, and what is missing

Two mechanisms already carry events outwards:

| Mechanism | What it is | What it does not do |
| --- | --- | --- |
| [Change feed](Transactional-Outbox.md) (`IAggregateChangeFeedProcessor`) | In-process callbacks around save and delete | Does not survive a restart, has no position, and cannot replay history |
| [Transactional outbox](Transactional-Outbox.md) (`IOutboxStore`) | Durable at-least-once hand-off, written in the save's transaction, leased and retried by `OutboxDispatchHostedService` | Only carries what a handler chose to enqueue **at write time**. A consumer added later sees nothing that happened before it existed |

The gap is a consumer that can be **added after the fact** and read the history: a projection
rebuild, a new read model, a late integration. That is what a subscription is for.

Retention is the other end of the same stream. Event streams currently grow without bound; nothing
removes an event, ever. For a long-lived aggregate that is a cost and a replay-time problem, and for
a regulated deployment it is a compliance problem that
[crypto-shredding](../../skills/event-sourcing-crypto-shredding/SKILL.md) only partly answers —
shredding makes a payload unreadable, it does not remove the row.

## The central difficulty: there is no global order

This framework's event streams are **per aggregate**. There is no ordered log across aggregates, and
no provider currently persists one:

- **Azure Table Storage** — `PartitionKey` is the aggregate id, `RowKey` encodes the aggregate
  version. Ordering exists only inside a partition. Azure Tables offers no cross-partition sequence.
- **SQL Server / PostgreSQL** — events are keyed by aggregate id and version. An identity or
  sequence column could be added, but does not exist.
- **MongoDB** — the same shape; `ObjectId` is roughly time-ordered but is not a gap-free sequence
  and is not safe to checkpoint against.
- **Cosmos DB** — no event stream at all (`no-event-stream` operational limitation).

So "subscribe to the event stream" is not a single design; it is a choice about what order a
subscriber is promised. Three options, honestly compared:

### Option A — per-aggregate catch-up, no global order

A subscription is scoped to one aggregate type and delivers each aggregate's events in version
order. Aggregates are visited in an unspecified order, discovered by enumerating the snapshot store
or an aggregate index.

- **Promise**: per-aggregate ordering only. Good enough for projections keyed by aggregate id, which
  is most of them.
- **Cost**: no new storage. Works on every provider that has an event stream.
- **Weakness**: a consumer that needs "everything that happened, in order" cannot have it. Discovery
  requires a way to enumerate aggregate ids, which Azure Tables does cheaply and others less so.

### Option B — a global sequence column, provider-side

Each provider gains a monotonic sequence on the event row, and a subscription is a cursor over it.

- **Promise**: total order within an aggregate type, or across all of them.
- **Cost**: a schema change on every provider, and **Azure Table Storage cannot do it** — there is
  no cross-partition atomic counter, and synthesising one serialises every write. Azure would have
  to be excluded, which breaks the parity rule the repository holds to.
- **Weakness**: gap-free sequences and concurrent writers interact badly. A reader that checkpoints
  at sequence *N* can miss a row committed later with a lower sequence, unless reads trail by a
  safety window or the sequence is assigned at commit. This is the classic correctness trap.

### Option C — subscriptions are outbox consumers

Keep the outbox as the only ordered, durable log, and make a subscription a second consumer group
over it, with its own checkpoint and retention.

- **Promise**: the outbox's existing ordering (creation time) and at-least-once delivery.
- **Cost**: smallest. Reuses leasing, retry, poison handling, and the transactional write.
- **Weakness**: **does not solve the actual gap.** The outbox only contains what was enqueued at
  write time, so a subscriber added later still sees nothing historical — unless every event is
  enqueued unconditionally, which makes the outbox a full copy of the event stream and doubles
  storage.

**Recommendation: Option A.** It is the only one that is both implementable on every provider with
an event stream and honest about what it promises. Option B can be added later for the relational
providers as a stronger, opt-in guarantee reported through `EventStoreCapabilities`, without
invalidating Option A's contract.

## Part 1 — Subscriptions (Option A)

### Contracts

```csharp
// Where a subscription has got to. Per subscription, per aggregate, per tenant.
public interface ISubscriptionCheckpointStore
{
    Task<long?> GetAsync(string subscriptionName, string aggregateId, CancellationToken cancellationToken);

    Task SetAsync(string subscriptionName, string aggregateId, long version, CancellationToken cancellationToken);
}

// What a consumer implements.
public interface IEventSubscriber<T>
    where T : class, IAggregate, new()
{
    string SubscriptionName { get; }

    Task OnEventAsync(EventSubscriptionContext<T> context, CancellationToken cancellationToken);
}
```

`EventSubscriptionContext<T>` carries the aggregate id, the `EventRecord`, its persisted type name,
the aggregate version and the `EventMetadata` — the same material `GetEventRangeAsync` already
returns, so nothing new has to be persisted.

Driving it is a hosted service shaped like `OutboxDispatchHostedService`: claim a batch, deliver,
checkpoint, back off. Reusing that shape rather than inventing a second one is deliberate.

### Delivery guarantee

**At-least-once, with per-aggregate ordering.** The checkpoint is written *after* the handler
returns, so a crash between the two redelivers. Subscribers must be idempotent, and the
documentation must say so as plainly as the outbox's does.

Exactly-once is not offered. It would require the checkpoint write to share the subscriber's
transaction, which the framework cannot see.

### Tenancy

Per the repository's rule that
[tenancy is tested first](Shared-Testing-Framework.md#tenant-isolation-is-part-of-the-contract):

- Checkpoints are **tenant-scoped rows**. Two tenants running the same subscription name must not
  share a position, or one tenant's progress silently skips the other's events.
- A subscription runs under a `TenantScope`. A tenant-scoped subscription sees only its tenant's
  aggregates; a `TenantScope.System` subscription sees all of them and must checkpoint per tenant
  regardless.
- An unowned aggregate (no tenant stamp) is invisible to a tenant-scoped subscription, matching the
  fail-closed read behaviour.

These are contract tests before they are code.

### Interaction with unknown events and upcasting

A subscriber may encounter an event type it cannot resolve — the same situation replay handles with
`SkippedEventRecord`. The subscription must not stall on it. Proposal: deliver it as an unresolved
event with its persisted type name, let the subscriber ignore it, and checkpoint past it.
Upcasters apply before delivery, so a subscriber sees current shapes.

### Open questions

1. How are aggregate ids enumerated for discovery? Azure Tables can scan partitions; the SQL
   providers would use the snapshot store. Is a snapshot store a **prerequisite** for subscriptions?
2. Does a subscription deliver events for **deleted** aggregates, and does it see the `Deleted`
   event?
3. One hosted service per subscription, or one driving all of them?

## Part 2 — Retention

### What it is

Remove events from a stream once they are no longer needed to reconstruct the aggregate — which is
only safe **once a snapshot covers them**, because the snapshot then becomes the replay base.

```csharp
public sealed record EventRetentionPolicy
{
    // Never prune events newer than this.
    public TimeSpan MinimumAge { get; init; } = TimeSpan.FromDays(90);

    // Always leave this many events behind the snapshot, as a safety margin.
    public int RetainedVersions { get; init; } = 100;

    // Refuse to prune at all unless a snapshot at or beyond the prune point exists.
    public bool RequireSnapshot { get; init; } = true;
}
```

### The hard constraint

**`RequireSnapshot` must not be defeasible.** Pruning an event with no snapshot behind it destroys
business state permanently, which violates the repository's first invariant: event streams are the
canonical source of truth and snapshots are replaceable optimisations. Retention is the one place
where that relationship inverts, and it must do so only when the snapshot provably exists and
provably covers the prune point.

A provider without snapshot support therefore cannot offer retention, and must report that rather
than silently no-op.

### Interactions

| With | Consequence |
| --- | --- |
| **Snapshots** | The snapshot becomes load-bearing. Its schema version must still be readable, so [snapshot schema versioning](Snapshot-Schema-Versioning.md) becomes a correctness concern, not just a convenience |
| **Crypto-shredding** | Retention and shredding overlap but are not substitutes. Shredding removes the key for a data subject; retention removes the row for everyone. A shredded event that is later pruned is fine; a pruned event cannot be shredded, so the snapshot must not retain the plaintext |
| **Subscriptions** | A subscription whose checkpoint is behind the prune point can never catch up. Retention must either refuse to prune past the slowest checkpoint, or report the subscription as permanently gapped. Refusing is safer and should be the default |
| **Upcasting** | Pruned events are never upcast again, so a future upcaster cannot reach them. Any schema migration must run before the events ageing out |
| **Tenancy** | Pruning runs per tenant or as a system operation, and must never prune across a tenant boundary |
| **Admin** | Retention is an operator action with a blast radius. It belongs behind the existing `Admin.Security` policy, and should report what it would remove before removing it |

### Open questions

1. Is retention **scheduled** (a hosted service) or **invoked** (an admin action)? Scheduled is more
   useful and considerably more dangerous.
2. Is a dry-run mode required before the first destructive release? (Strong recommendation: yes.)
3. Does pruning interact with the audit expectations some deployments have — is there a jurisdiction
   where removing an event is itself a problem?

## Provider impact

| Provider | Subscriptions (Option A) | Retention |
| --- | --- | --- |
| Azure Storage | Yes — partition scan for discovery | Yes — snapshot-backed, blob snapshots |
| MongoDB | Yes | Yes |
| PostgreSQL | Yes | Yes |
| SQL Server | Yes | Yes |
| Cosmos DB | **No** — `no-event-stream` | **No** — nothing to prune |
| InMemory | Yes — needed for the acceptance scenarios | Yes |

Both features need new flags on `EventStoreCapabilities` (`SupportsSubscriptions`,
`SupportsRetention`) so an application can discover the truth rather than infer it, in line with how
every other capability is reported.

## What the work includes

Beyond the implementation, per the repository's rules:

- Shared contract tests in `EventStoreContractTestsBase`, **tenancy cases first**, with an abstract
  seam per provider so no provider can be added without stating its behaviour.
- Acceptance scenarios in `EventSourcing.AcceptanceTests` stating the guarantees in domain language
  — particularly "a subscription added today sees what happened yesterday" and "retention never
  removes an event that is still needed".
- A `skills/event-sourcing-subscriptions` skill, and retention guidance added to
  `skills/event-sourcing-crypto-shredding` where the two meet.
- Updates to [Provider Feature Matrix](Provider-Feature-Matrix.md),
  [Provider Capabilities](Provider-Capabilities.md),
  [Guarantees and Limitations](Guarantees-and-Limitations.md) and the provider guides.

## Decisions needed before implementation

1. **Option A, B or C** for ordering. (Recommendation: A.)
2. Whether a **snapshot store is a prerequisite** for subscriptions, which decides the discovery
   mechanism.
3. Whether retention is **scheduled or operator-invoked**, and whether a dry run ships first.
4. Whether both halves ship together, or **subscriptions first** — they are independent, and
   retention is the one that can destroy data.
