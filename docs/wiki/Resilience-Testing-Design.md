# Resilience Testing — Design

> **Status: phase 1 implemented.**
>
> `FaultPlan` and `FaultInjectingEventStore<T>` ship in `SharedTestingFramework`, with the failure
> invariants as unit tests running on every pull request — see
> [Shared Testing Framework](Shared-Testing-Framework.md#fault-injection). Phase 2 (Admin endpoint
> security contract tests) is also implemented. **Phases 4 and 5 remain proposals**: transport-level
> faults and the load/soak harness do not exist.

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

- **Admin surface is thinly tested relative to its risk** — partly closed. The per-endpoint tests
  cover the endpoints that exist today; `AdminEndpointSecurityContractTests` now covers the *set*,
  enumerating `EndpointDataSource` and asserting that every Admin endpoint requires authorization,
  **names a policy**, sits under the configured prefix, and refuses a principal entitled to nothing.

  The middle one is the assertion that bites. The route group applies a blanket
  `RequireAuthorization()`, which is authentication only — any signed-in caller passes it. An
  endpoint that forgets its own `RequireAuthorization(policyName)` therefore stays "protected" to
  every other test while being readable by anyone who can log in. Verified by removing one, which
  failed that test and nothing else.
- **No Admin integration tests** for the MongoDB, Azure or Postgres adapters.
- **Uneven provider depth** — `SqlServer.IntegrationTests` has 15 files, `AzureStorage.IntegrationTests` 3.
- **Cosmos DB is never validated against the real service**; the vnext emulator cannot run the
  suite. Already documented, still a risk.

## Phases

| Phase | Work |
| --- | --- |
| 1 | `FaultInjectingEventStore<T>` and `FaultPlan`; the first six invariants above as unit tests |
| 2 | **Done.** `AdminEndpointSecurityContractTests` asserts the security properties of the whole mapped endpoint set, so an endpoint added later is covered without anyone remembering |
| 3 | ~~An upgrade/compatibility suite~~ - out of scope: 2.0 targets new deployments and supports no migration path |
| 4 | Transport fault injection in one provider as a pilot, then the rest |
| 5 | Load and soak harness |

## Decisions needed

1. Whether fault injection lives in `SharedTestingFramework` (reusable by provider suites) or only
   in the unit project. (Recommendation: `SharedTestingFramework`.)
2. Which transport-fault tool — Toxiproxy adds a container per suite; SDK-level hooks are
   provider-specific and less realistic.
4. Whether load/soak runs anywhere automatically, or purely on demand.
