# Production Readiness — Status

Single source of truth for the production-readiness programme: what has shipped, what has not, and
what is waiting on a decision. The five design pages hold the *reasoning*; this page holds the
*state*, so it is the one to read first and the one to update when something lands.

**Last verified against the code: 2026-10-26**, on branch
`feat/2.0-results-tenancy-subscriptions` (PR #60, 25 commits ahead of `main`).

## Where each plan stands

| Plan | Status | What is left |
| --- | --- | --- |
| [Crypto Key Management](Crypto-Key-Management-Design.md) | **Done** | Rotation ships for the two SQL providers only |
| [Observability Parity](Observability-Parity-Design.md) | **Done, one gap** | No test asserts spans are emitted |
| [Durable Admin Audit](Admin-Audit-Durability-Design.md) | **Partly done** | Phases 3–5: read side, Azure/Mongo loggers, retention |
| [Retention](Retention-Design.md) | **Done** | Retired subscriptions leave checkpoint rows nothing deletes |
| [Resilience Testing](Resilience-Testing-Design.md) | **Phase 1 done** | Phases 2–5: transport faults, Admin coverage, upgrade suite, load/soak |

Also delivered outside those five: the result/failure model, multi-tenancy, crypto-shredding,
metrics, principal capture, tenant isolation as a provider contract, the BDTest acceptance suite, and
[subscriptions](Subscriptions.md) with durable checkpoints on every provider that has an event stream.

## Remaining work, in the order I would do it

### 1. Retention — nothing removes anything, ever

Three things grow without bound, and the first is the cheapest and safest to fix:

| What | Risk | Notes |
| --- | --- | --- |
| ~~Idempotency markers~~ | **Done** | `IIdempotencyMarkerMaintenance`, all four providers. Nothing schedules it yet |
| **Admin audit entries** | Low | Added by the audit work; needs a *minimum* retention floor rather than a maximum |
| ~~Event streams~~ | **Done** | Preview, policy and prune ship. `RequireSnapshot = false` is permitted but logs a warning naming the risk |

Verified absent: no `PruneAsync`, no `EventRetentionPolicy`, no marker cleanup. The transactional
outbox's `CleanupAsync` is the only retention that exists, and is the precedent to follow.

### 2. Resilience testing — the failure modes that corrupt an event store are untested

~1,460 tests, all happy path and expected failures. Nothing covers a save that commits events then
fails the snapshot, a lost response causing a retry, a lease expiring mid-dispatch, or real write
contention. Verified absent: no fault injection of any kind.

Phase 1 (`FaultInjectingEventStore<T>` plus the seven invariants) is the valuable part and runs in
the unit suite.

### 3. Admin audit, phases 3–5

No read side (`IAdminAuditQueryService` does not exist — verified), no Azure Storage or MongoDB
logger, no retention floor.

### 4. Smaller, already-decided follow-ups

- **Subscription dead-letter.** A permanently failing event stalls its stream forever. Fail-closed
  was deliberate; an escape hatch that does not lose data silently is still missing.
- **Key rotation beyond SQL.** Azure Storage, MongoDB and both Key Vault stores do not implement
  `IRotatingEventCryptoKeyStore`.
- **Span assertions.** The provider fixtures inject mocked telemetry, so a test listening for real
  activities records nothing. Needs a fixture using the generated telemetry.
- **Ordered live tail.** Option D in the
  [subscriptions design](Subscriptions-And-Retention-Design.md); needs per-key outbox leasing with
  head-of-line blocking.

## Decisions that block work

Each design page ends with its own list. These are the ones that stop work starting:

| # | Decision | My recommendation |
| --- | --- | --- |
| ~~1~~ | ~~Retention: **scheduled or operator-invoked**?~~ | **Answered: both, as independent flags** |
| ~~2~~ | ~~Retention: does a **dry run ship before** the destructive path?~~ | **Answered: the preview shipped first** |
| ~~3~~ | ~~Retention: is `RequireSnapshot = false` permitted at all?~~ | **Answered: yes, with a warning logged every run** |
| ~~4~~ | ~~Retention: **blocked by** a lagging subscription checkpoint, or warn?~~ | **Answered: blocked** |
| ~~5~~ | ~~Resilience: does fault injection live in `SharedTestingFramework`?~~ | **Answered: yes, authored there and linked into the unit suite** |
| 6 | Audit: is the **query endpoint** part of the Admin API, or database-only? | Open — it is a second privilege either way |

## Known gaps outside the five plans

Found while doing other work, deliberately not acted on. Recorded so they are not rediscovered as
surprises.

- **`PostgresOutboxStore` has an unguarded `CREATE TABLE IF NOT EXISTS` race.** The same defect that
  was fixed in the subscription checkpoint and crypto key stores: PostgreSQL races on the table's
  implicit row type and raises `23505`. A real latent bug in shipped code.
- **Azure Storage blob-backed snapshots are not directly tenant-filtered.** Protected transitively,
  which is weaker than it sounds.
- **`.agents/skills/` is tracked** — 59 files, duplicated into `.claude/skills/` — although
  `AGENTS.md` documents it as gitignored.
- **Cosmos DB is never validated against the real service.** The vnext emulator cannot run the suite.
- **No upgrade test.** Nothing proves data written by 1.x reads under 2.0, on a branch whose headline
  change is breaking.
- **`aggregate-design` and `value-objects` skill bodies** were carried forward during the skills
  migration rather than rethought.

## How this page is kept honest

Every status above was checked against the code on the date at the top, not recalled. When a plan
moves, update its own status banner *and* this table — a page that says "proposal" while the feature
exists is worse than no page.
