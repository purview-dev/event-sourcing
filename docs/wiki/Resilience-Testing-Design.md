# Resilience Testing — Design

> **Status: proposal. Nothing on this page is implemented.**

## The problem

The suite is broad — ~1,450 tests, per-provider contract suites, acceptance scenarios — but it only
tests the **happy path and expected failures**. The failure modes that actually corrupt an event
store are the ones no current test reaches:

- A save that commits events but fails writing the snapshot.
- A storage call that succeeds server-side but whose response is lost, so the client retries.
- An outbox lease that expires mid-dispatch, so two dispatchers hold the same message.
- A provider that times out under load rather than failing fast.
- Two writers racing on the same aggregate under real contention, not simulated.

Every one of these is a correctness question, and all of them are currently unanswered. Three times
during this work a filter looked correct, was not, and the suite stayed green — the same category of
blind spot.

There is also **no load or soak testing**. The performance harness under `src/src/Benchmarks`
measures throughput; it does not assert that behaviour stays correct while under it.

## Design

Three layers, cheapest first.

### 1. Fault injection at the store boundary

A decorating `IEventStoreCore<T>` that fails on command, composed in test wiring only:

```csharp
public sealed class FaultInjectingEventStore<T>(IEventStoreCore<T> inner, FaultPlan plan) : IEventStoreCore<T>;
```

`FaultPlan` describes *when* and *how*: fail the nth call to a named operation, throw a specific
exception, delay, or succeed-then-report-failure (the lost-response case, which is the one that
produces duplicates).

This covers the framework's own logic — retry behaviour, transaction rollback, outbox state
transitions — without a container. Fast enough to live in the unit suite.

### 2. Fault injection at the transport

Some failures only exist below the abstraction: a connection reset mid-read, a partial write, a
server-side timeout. These need a proxy between the client and the container (Toxiproxy, or the
provider SDK's own fault hooks) and belong in the integration suites, gated behind a category so
they do not run on every PR.

### 3. Load and soak

A harness that runs the contract suite's invariants **while** under concurrent load, asserting that
the invariants still hold rather than measuring a number:

- N writers on the same aggregate: exactly one wins per version, no event is lost, no version is
  duplicated.
- N writers across M aggregates for a sustained period: event count reconciles, no orphaned
  snapshots, outbox drains to empty.
- A subscription keeps up or falls behind, but never skips.

Soak matters for the leaks that only appear over time — unbounded idempotency markers, outbox rows
that never reach a terminal state, connections not returned to the pool.

## The invariants worth asserting under fault

These are the assertions; the mechanism above exists to make them testable.

| Invariant | Fault that threatens it |
| --- | --- |
| An event is never lost once a save reports success | Snapshot write fails after event commit |
| An event is never applied twice | Lost response, client retries |
| Stream version is contiguous, never gapped | Concurrent writers, partial batch |
| A failed save leaves no partial state | Transaction rollback, batch split on Azure |
| The outbox delivers at least once, never zero | Lease expiry mid-dispatch |
| A subscription never skips an event | Checkpoint written before handler completes |
| Tenant isolation holds | Any of the above, under a tenant scope |

The last line is the point: **every resilience test should run under a tenant scope**, because a
failure path is exactly where a tenant stamp gets forgotten.

## Placement

| Layer | Project | Category |
| --- | --- | --- |
| Store-boundary faults | `EventSourcing.UnitTests` | `Unit` — runs in PR validation |
| Transport faults | `<Provider>.IntegrationTests` | New `Resilience` category, opt-in |
| Load and soak | New `EventSourcing.ResilienceTests` console harness under `src/src/Benchmarks` | Not a test project; run on demand |

Only the first runs in CI. The other two are too slow and too infrastructure-dependent, which is the
same reasoning that keeps the provider integration suites out of PR validation.

## Other gaps worth closing alongside

Found during the same assessment, cheaper than the above and arguably higher value per hour:

- **Admin surface is thinly tested relative to its risk** — 7 files in `Admin.UnitTests` for the
  whole API and authorization model; `Admin.SqlServer.UnitTests` has 2. This is the security
  boundary.
- **No Admin integration tests** for the MongoDB, Azure or Postgres adapters.
- **Uneven provider depth** — `SqlServer.IntegrationTests` has 15 files, `AzureStorage.IntegrationTests` 3.
- **No upgrade test** — nothing proves data written by 1.x reads under 2.0, which matters for a
  release carrying breaking changes.
- **Cosmos DB is never validated against the real service**; the vnext emulator cannot run the
  suite. Already documented, still a risk.

## Phases

| Phase | Work |
| --- | --- |
| 1 | `FaultInjectingEventStore<T>` and `FaultPlan`; the first six invariants above as unit tests |
| 2 | Admin test coverage up to the level its risk warrants |
| 3 | An upgrade/compatibility suite reading 1.x-shaped data |
| 4 | Transport fault injection in one provider as a pilot, then the rest |
| 5 | Load and soak harness |

## Decisions needed

1. Whether fault injection lives in `SharedTestingFramework` (reusable by provider suites) or only
   in the unit project. (Recommendation: `SharedTestingFramework`.)
2. Which transport-fault tool — Toxiproxy adds a container per suite; SDK-level hooks are
   provider-specific and less realistic.
3. Whether the upgrade suite needs **committed fixture data** in 1.x shape, which is a maintenance
   commitment but the only way to test it honestly.
4. Whether load/soak runs anywhere automatically, or purely on demand.
