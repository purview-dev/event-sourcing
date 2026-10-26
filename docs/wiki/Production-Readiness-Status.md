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
| [Observability Parity](Observability-Parity-Design.md) | **Done, one gap** | Span emission asserted on SQL Server only; the other three use mocked telemetry |
| [Durable Admin Audit](Admin-Audit-Durability-Design.md) | **Phases 1–4 done** | Phase 5: retention floor. Nothing deletes an audit entry, ever |
| [Retention](Retention-Design.md) | **Done** | Nothing schedules marker cleanup or checkpoint removal; both are operator-invoked |
| [Resilience Testing](Resilience-Testing-Design.md) | **Phases 1, 2, 4 (pilot) done** | Transport faults wired for SQL Server only; no load/soak harness |

Also delivered outside those five: the result/failure model, multi-tenancy, crypto-shredding,
metrics, principal capture, tenant isolation as a provider contract, the BDTest acceptance suite,
[subscriptions](Subscriptions.md) with durable checkpoints on every provider that has an event
stream, subscription dead-lettering, and checkpoint removal for retired subscriptions.

## What is genuinely left

Everything below is a deliberate gap, not an oversight. Each is stated in the page that owns it.

| Gap | Why it is open |
| --- | --- |
| **Admin audit retention** | Nothing deletes an entry, on any provider. Safe by default, unbounded by default. No delete path is offered that could be mistaken for a sanctioned one |
| **Load and soak** | Nothing asserts the invariants hold *while* under concurrent load. The benchmarks measure throughput, not correctness under it |
| **Transport faults beyond SQL Server** | `FaultingTcpProxy` is wired for one provider as a pilot |
| **Span assertions beyond SQL Server** | The other fixtures inject mocked telemetry, so a listener sees nothing |
| **Crypto key rotation beyond the SQL providers** | Azure Storage, MongoDB and both Key Vault stores do not implement `IRotatingEventCryptoKeyStore` |
| **No durable dead-letter sink** | The contract ships; the sink is the application's. Attempt counts are in memory and reset on restart |
| **Ordered live tail** | Needs per-key outbox leasing with head-of-line blocking |
| **Nothing schedules the maintenance operations** | Marker cleanup and checkpoint removal are operator-invoked by design |

## How each area got here

### 1. Retention — what grows, and what now removes it

| What | Status | Notes |
| --- | --- | --- |
| ~~Idempotency markers~~ | **Done** | `IIdempotencyMarkerMaintenance`, all four providers. Nothing schedules it |
| ~~Event streams~~ | **Done** | Preview, policy and prune ship. `RequireSnapshot = false` is permitted but logs a warning naming the risk |
| ~~Subscription checkpoints~~ | **Done** | `ISubscriptionCheckpointMaintenance`, all five stores. Operator-invoked, no wildcard |
| **Admin audit entries** | **Open** | Needs a *minimum* retention floor rather than a maximum — the risk is deleting too early, not growing too large |

Nothing schedules any of these, deliberately: each one redelivers, replays or destroys something if
run at the wrong moment, and a timer is a poor judge of the right moment.

### 2. Resilience testing — mostly closed

Three layers now exist:

| Layer | What | Where it runs |
| --- | --- | --- |
| Store boundary | `FaultInjectingEventStore<T>` + seven invariants | Unit suite, every PR |
| Admin surface | `AdminEndpointSecurityContractTests` over the whole mapped endpoint set | Unit suite, every PR |
| Transport | `FaultingTcpProxy` severs the connection beneath the SDK | SQL Server integration only |

Still missing: **load and soak** — nothing asserts the invariants still hold *while* under
concurrent load — and transport faults for PostgreSQL, MongoDB and Azure Storage.

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
- ~~**Retired subscriptions leave checkpoint rows.**~~ **Done.** `ISubscriptionCheckpointMaintenance`
  on all five stores, operator-invoked, scope matched exactly.
- ~~**`PostgresOutboxStore` `CREATE TABLE IF NOT EXISTS` race.**~~ **Fixed**, matching the checkpoint
  and crypto key stores.
- **Key rotation beyond SQL.** Azure Storage, MongoDB and both Key Vault stores do not implement
  `IRotatingEventCryptoKeyStore`.
- **Span assertions, on the other three providers.** SQL Server now has a fixture that injects the
  generated telemetry and a test that listens for the activities; PostgreSQL, MongoDB and Azure
  Storage still use the mock, so their spans are covered by call-level assertions only.
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
- **An unexplained intermittent in the Postgres checkpoint-maintenance contract tests.**
  `RemoveSubscriptionAsync_LeavesAnotherTenantsCheckpointsAlone` and its system-scope twin failed in
  two of three *full-suite* runs, and have never failed when the class runs alone. The failure is an
  assertion - the other tenant's row is absent after the delete - not an exception, and the delete
  predicate names both the subscription and the tenant, so it cannot match the other tenant's row.
  Three full-suite runs have been clean since the tests gained an assertion *before* the delete,
  which separates "the write never landed" from "the delete over-matched"; that assertion has not
  fired, so the mechanism is still unknown and **this is recorded as open, not fixed**. Next time it
  fires, which half it fires in is the answer.
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
