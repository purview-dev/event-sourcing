# Production Readiness — Status

Single source of truth for the production-readiness programme: what has shipped, what has not, and
what is waiting on a decision. The five design pages hold the *reasoning*; this page holds the
*state*, so it is the one to read first and the one to update when something lands.

**Last verified against the code: 2026-10-26**, on branch
`feat/2.0-results-tenancy-subscriptions` (PR #60).

## Where each plan stands

| Plan | Status | What is left |
| --- | --- | --- |
| [Crypto Key Management](Crypto-Key-Management-Design.md) | **Done** | Rotation ships for the two SQL providers only |
| [Observability Parity](Observability-Parity-Design.md) | **Done, one gap** | No test asserts spans are emitted |
| [Durable Admin Audit](Admin-Audit-Durability-Design.md) | **Phases 1–4 done** | Phase 5: retention floor. Nothing deletes an audit entry, ever |
| [Retention](Retention-Design.md) | **Done** | Retired subscriptions leave checkpoint rows nothing deletes |
| [Resilience Testing](Resilience-Testing-Design.md) | **Phases 1–2 done** | Phases 4–5: transport faults, load/soak |

Also delivered outside those five: the result/failure model, multi-tenancy, crypto-shredding,
metrics, principal capture, tenant isolation as a provider contract, the BDTest acceptance suite, and
[subscriptions](Subscriptions.md) with durable checkpoints on every provider that has an event stream.

## Remaining work, in the order I would do it

### 1. Retention — nothing removes anything, ever

Three things grow without bound, and the first is the cheapest and safest to fix:

| What | Risk | Notes |
| --- | --- | --- |
| ~~Idempotency markers~~ | **Done** | `IIdempotencyMarkerMaintenance`, all four providers. Nothing schedules it yet |
| **Admin audit entries** | Low | Still open. Needs a *minimum* retention floor rather than a maximum — the risk is deleting too early, not growing too large |
| ~~Event streams~~ | **Done** | Preview, policy and prune ship. `RequireSnapshot = false` is permitted but logs a warning naming the risk |

Verified absent: no `PruneAsync`, no `EventRetentionPolicy`, no marker cleanup. The transactional
outbox's `CleanupAsync` is the only retention that exists, and is the precedent to follow.

### 2. Resilience testing — the failure modes that corrupt an event store are untested

~1,460 tests, all happy path and expected failures. Nothing covers a save that commits events then
fails the snapshot, a lost response causing a retry, a lease expiring mid-dispatch, or real write
contention. Verified absent: no fault injection of any kind.

Phase 1 (`FaultInjectingEventStore<T>` plus the seven invariants) is the valuable part and runs in
the unit suite.

### 3. Admin audit, phase 5 only

Phases 3 and 4 shipped: `IAdminAuditEntryReader` with tenant-filtered implementations for SQL Server,
PostgreSQL, Azure Storage, MongoDB and the in-memory sink, and durable loggers for all four. The
Azure Storage one partitions by tenant rather than filtering on it.

What is left is the **retention floor**: nothing deletes an audit entry on any provider. Safe by
default, unbounded by default. No delete path is offered deliberately — one that could be mistaken
for sanctioned pruning is worse than none.

There is **no Admin API endpoint** for reading the trail, by decision (see decision 6 below).

### 4. Smaller, already-decided follow-ups

- ~~**Subscription dead-letter.**~~ **Done.** Off by default and needs two deliberate steps
  (`MaxDeliveryAttempts` plus a registered sink); the checkpoint advances only when the sink confirms
  it recorded the failure. What is *not* done: no durable sink ships, and attempt counts are
  in-memory so they reset on restart.
- **Key rotation beyond SQL.** Azure Storage, MongoDB and both Key Vault stores do not implement
  `IRotatingEventCryptoKeyStore`.
- **Span assertions.** The provider fixtures inject mocked telemetry, so a test listening for real
  activities records nothing. Needs a fixture using the generated telemetry.
- **Ordered live tail.** Option D in the
  [subscriptions design](Subscriptions-And-Retention-Design.md); needs per-key outbox leasing with
  head-of-line blocking.

## Decisions that block work

Every decision below is now answered. Each design page keeps its own list.

| # | Decision | My recommendation |
| --- | --- | --- |
| ~~1~~ | ~~Retention: **scheduled or operator-invoked**?~~ | **Answered: both, as independent flags** |
| ~~2~~ | ~~Retention: does a **dry run ship before** the destructive path?~~ | **Answered: the preview shipped first** |
| ~~3~~ | ~~Retention: is `RequireSnapshot = false` permitted at all?~~ | **Answered: yes, with a warning logged every run** |
| ~~4~~ | ~~Retention: **blocked by** a lagging subscription checkpoint, or warn?~~ | **Answered: blocked** |
| ~~5~~ | ~~Resilience: does fault injection live in `SharedTestingFramework`?~~ | **Answered: yes, authored there and linked into the unit suite** |
| ~~6~~ | ~~Audit: is the **query endpoint** part of the Admin API, or database-only?~~ | **Answered: no endpoint.** `IAdminAuditEntryReader` is public and registered; who may read the record of who read what is the deployment's decision. Adding an endpoint later is additive |

## Known gaps outside the five plans

Found while doing other work, deliberately not acted on. Recorded so they are not rediscovered as
surprises.

- **Azure Storage blob-backed snapshots are not directly tenant-filtered.** Protected transitively,
  which is weaker than it sounds.
- **`.agents/skills/` is tracked** — 59 files, duplicated into `.claude/skills/` — although
  `AGENTS.md` documents it as gitignored.
- **Cosmos DB is never validated against the real service.** The vnext emulator cannot run the suite.
- **No migration path from 1.x, by decision.** 2.0 targets new deployments. There is deliberately no
  upgrade suite, and schema changes are not expressed as migrations for existing databases - do not
  reintroduce either as "missing".
- **`aggregate-design` and `value-objects` skill bodies** were carried forward during the skills
  migration rather than rethought.

## How this page is kept honest

Every status above was checked against the code on the date at the top, not recalled. When a plan
moves, update its own status banner *and* this table — a page that says "proposal" while the feature
exists is worse than no page.
