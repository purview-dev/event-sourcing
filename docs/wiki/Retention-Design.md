# Retention — Design

> **Status: phases 1 and 2 implemented.**
>
> Idempotency marker cleanup ships for SQL Server, PostgreSQL, Azure Storage and MongoDB, and the
> event-stream retention **preview** ships provider-neutrally. **Phases 3 to 5 remain proposals** —
> nothing prunes an event. Decision 2 below is answered by construction: the dry run shipped first.
> Decisions 1, 3 and 4 are still open and block the destructive path.
>
> Supersedes Part 2 of [Subscriptions and Retention](Subscriptions-And-Retention-Design.md), which
> covered event-stream retention only.

## The problem

Nothing in the framework ever removes anything. Three things grow without bound:

| What | Growth | Consequence |
| --- | --- | --- |
| **Event streams** | One row per event, forever | Replay cost and storage cost rise with aggregate age |
| **Idempotency markers** | One row per save, forever | Pure overhead after the dedupe window has passed |
| **Outbox messages** | Bounded — `IOutboxStore.CleanupAsync` exists | Already solved; the precedent for the rest |

The outbox already shows the shape: `Task<int> CleanupAsync(TimeSpan retention, CancellationToken)`,
removing terminal messages older than a window. The other two need the same treatment, with very
different risk profiles.

## Idempotency markers — the easy half

A marker exists to deduplicate a retried save. Its usefulness expires with the retry window, which
is minutes, not years. After that it is dead weight that is never read.

```csharp
Task<int> CleanupIdempotencyMarkersAsync(TimeSpan retention, CancellationToken cancellationToken);
```

Default retention of 7 days is generous against any plausible retry window.

Deleting an expired marker is **safe**: the worst case is that a save retried after the window
elapsed is applied twice, which optimistic concurrency already rejects. That is why this half can
ship first and independently.

MongoDB and Azure Storage can use native TTL (a TTL index; `DeleteAfter` on the entity) rather than
a sweep. The SQL providers need a scheduled delete.

## Event streams — the dangerous half

Removing an event is the one operation that can destroy business state permanently, because the
event stream is the canonical source of truth. It is only safe once a **snapshot covers the prune
point**, at which moment the snapshot becomes the replay base.

```csharp
public sealed record EventRetentionPolicy
{
    /// Never prune events newer than this.
    public TimeSpan MinimumAge { get; init; } = TimeSpan.FromDays(90);

    /// Always leave this many events behind the snapshot, as a safety margin.
    public int RetainedVersions { get; init; } = 100;

    /// Refuse to prune unless a snapshot at or beyond the prune point exists.
    public bool RequireSnapshot { get; init; } = true;
}
```

### `RequireSnapshot` must not be defeasible

Pruning with no snapshot behind it destroys state irrecoverably and inverts the repository's first
invariant. The option exists to be *read*, not to be turned off: the implementation should treat
`false` as a request that requires a second, explicitly destructive opt-in, or refuse it entirely.

A provider without snapshot support therefore cannot offer stream retention, and must report that
rather than silently no-op.

### Interactions

| With | Consequence |
| --- | --- |
| **Snapshots** | The snapshot becomes load-bearing, so [snapshot schema versioning](Snapshot-Schema-Versioning.md) becomes a correctness concern rather than a convenience |
| **Subscriptions** | A subscription whose checkpoint is behind the prune point can never catch up. Retention must refuse to prune past the slowest checkpoint — not merely warn |
| **Crypto-shredding** | Complementary, not substitutes: shredding removes the key for one subject, retention removes rows for everyone. A pruned event can no longer be shredded, so the snapshot must not retain plaintext |
| **Upcasting** | Pruned events are never upcast again, so a schema migration must run *before* the events it touches age out |
| **Tenancy** | Pruning runs per tenant or as a system operation, and must never cross a tenant boundary |

The subscription interaction is the one most likely to be got wrong, because it is invisible: a
projection that is merely slow becomes a projection that is permanently incomplete, with no error.

## Operating model

Two questions, and I would answer both conservatively:

**Scheduled or invoked?** Scheduled is more useful and considerably more dangerous — an unattended
job that deletes history is a bad default. Recommendation: **operator-invoked through the Admin API
first**, behind its own permission and audited; a scheduled host service later, opt-in.

**Dry run?** Yes, and it should ship *before* the destructive path:

```csharp
Task<RetentionPreview> PreviewAsync(EventRetentionPolicy policy, CancellationToken cancellationToken);
```

returning what would be removed — counts per aggregate, the oldest and newest version affected, and
any stream blocked by a lagging subscription or a missing snapshot. A preview that reports blockers
is how an operator learns the policy is wrong before it costs them.

## Provider support

| Provider | Marker cleanup | Stream retention |
| --- | --- | --- |
| SQL Server, PostgreSQL | Scheduled delete | Yes — snapshot-backed |
| MongoDB | TTL index | Yes |
| Azure Storage | Entity TTL | Yes, plus blob cleanup for large payloads |
| Cosmos DB | n/a | n/a — no event stream |
| InMemory | Yes | Yes, for the acceptance scenarios |

Azure needs care: an oversized event is stored as a blob behind a pointer event, so pruning the
pointer must also delete the blob or it leaks storage silently.

## Phases

| Phase | Work |
| --- | --- |
| 1 | Idempotency marker cleanup, all providers — safe and independently valuable |
| 2 | `EventRetentionPolicy`, `PreviewAsync`, and the blocker checks (snapshot coverage, subscription lag) |
| 3 | Destructive prune, operator-invoked through the Admin API, audited |
| 4 | Azure blob cleanup for pruned large payloads |
| 5 | Optional scheduled host service, opt-in |

## Testing

- **Tenancy first**: pruning as tenant A leaves tenant B's streams untouched; the system scope can
  prune across tenants.
- Pruning with no covering snapshot is refused, and the aggregate still loads afterwards.
- After a prune, the aggregate rehydrates **from the snapshot plus the remaining events** to exactly
  the state it had before.
- A stream with a lagging subscription checkpoint is reported as blocked and not pruned.
- A pruned Azure stream leaves no orphaned blobs.
- `PreviewAsync` and the destructive path agree: what preview reported is what prune removed.
- Marker cleanup does not remove a marker still inside the retry window.

## Decisions needed

1. **Scheduled or operator-invoked** for stream retention. (Recommendation: invoked first.)
2. Whether **dry run ships before** the destructive path. (Recommendation: yes.)
3. Whether `RequireSnapshot = false` is permitted at all, and if so behind what second opt-in.
4. Whether retention is **blocked by** a lagging subscription or merely warns. (Recommendation:
   blocked.)
5. Whether any jurisdiction in scope forbids removing an event, which would make stream retention
   opt-in per aggregate type rather than per deployment.
