# Result and Failure Model

> **Status: implemented.** Every step in the rollout table below is done. This page is now the reference
> for the failure vocabulary the event store returns, and the record of the decisions that shaped it.

## Why

Three problems in the model this replaced.

**Failure types are duplicated per provider.** `ConcurrencyException`, `AggregateDeletedException`,
`AggregateLockedException`, `AggregateNotDeletedException`, `AggregateAlreadyDeletedException`,
`MissingAggregateIdException` and `CommitException` each exist **four or five times** — once in
`AzureStorage`, `MongoDB`, `Postgres`, `SqlServer`, and `InMemory` for concurrency. They share no base type.
A caller cannot write a provider-portable `catch`, which is why the empty marker interface
`IConcurrencyConflict` exists at all: it is the one bridge, and it only covers concurrency.

**`bool` conflates "nothing to do" with "failed".** `SaveResult<TAggregate>` converts implicitly to `bool`,
and its own documentation says a `false` "does not always mean failure, it may mean there was no events to
save, or the events were already saved — in the case where idempotency markers are recorded and validated".
Two very different outcomes, one value.

**`null` conflates "absent" with "deleted".** `GetAsync` returned `T?`, and whether a soft-deleted aggregate
came back as `null` or threw was controlled by `EventStoreOperationContext.DeleteMode`. The caller could not
tell the cases apart from the return value.

Results fix all three at once: the vocabulary becomes a small set of **core, shared, provider-neutral types**,
and the compiler forces callers to handle them.

## Principle

> A failure that a correct caller can anticipate and act on is a **result case**.
> A failure that means the system, the configuration or the calling code is wrong is an **exception**.

Cancellation is always an exception (`OperationCanceledException`), as everywhere else in .NET.

## Case vocabulary

Core types in `Purview.EventSourcing`, each a small `readonly record struct` carrying the identity needed to
act on it. Versions are `long` from the outset, so the Phase 6 `int`→`long` widening is not a second break.

| Case | Payload | Meaning |
| --- | --- | --- |
| `AggregateNotFound` | type, id | No stream exists |
| `AggregateDeleted` | type, id | The stream exists and is soft-deleted |
| `AggregateNotDeleted` | type, id | Restore, or `GetDeletedAsync`, attempted on a live aggregate |
| `AggregateAlreadyDeleted` | type, id | Delete attempted on an already-deleted aggregate |
| `AggregateLocked` | type, id | The aggregate is locked against modification |
| `ConcurrencyConflict` | type, id, expected version, actual version | Optimistic concurrency lost |
| `AggregateInvalid` | `ValidationResult` | Validation rejected the aggregate before any write |

## Unions

One union per **operation family**, not per method. Cases are deliberately shared between families.

```csharp
[GenerateResult]
public readonly union AggregateReadFailure(
    AggregateNotFound, AggregateDeleted, AggregateNotDeleted, AggregateLocked);

[GenerateResult]
public readonly union AggregateSaveFailure(
    ConcurrencyConflict, AggregateDeleted, AggregateLocked, AggregateInvalid);

[GenerateResult]
public readonly union AggregateDeleteFailure(
    AggregateNotFound, AggregateAlreadyDeleted, AggregateLocked, ConcurrencyConflict);

[GenerateResult]
public readonly union AggregateRestoreFailure(
    AggregateNotFound, AggregateNotDeleted, AggregateLocked, ConcurrencyConflict);
```

**Authoring rule that follows from the sharing.** A leaf case listed in more than one union gets its per-case
`AsFailure<T>()` helper generated only once, and the generator reports `RSG1006`. Construct shared cases
through the union-receiver factory, which always names the union you mean:

```csharp
// Correct - names the union.
return AggregateSaveFailure.Failure<Order>(new AggregateLocked(type, id));

// Wrong for a shared case - can bind to a different union's helper.
return new AggregateLocked(type, id).AsFailure<Order>();
```

Cases unique to one union may use the shorter `AsFailure<T>()` form.

## What stays an exception

| Type | Why |
| --- | --- |
| `CommitException` | Storage refused the write — infrastructure, not an expected outcome |
| `AggregateStreamVersionMissingException` | The stream is corrupt; no caller can sensibly continue |
| `MissingAggregateIdException`, `IdAlreadySetException` | Programming errors in aggregate construction |
| `UnregisteredEventException` | The aggregate never registered the event — a wiring defect |
| `EventStoreTransactionGuaranteeException` | A required guarantee the provider cannot honour: a startup contract violation |
| `InvalidPartitionKeyException`, `TableOperationException` | Provider-level infrastructure faults |

## Mapping from today

| Today | Tomorrow |
| --- | --- |
| `ConcurrencyException` ×5 | one core `ConcurrencyException`, translated at the provider boundary into the `ConcurrencyConflict` case. The type and the `IConcurrencyConflict` marker remain for the paths that still raise one; no save throws it |
| `AggregateDeletedException` / `AggregateIsDeletedException` ×4 | `AggregateDeleted` case |
| `AggregateAlreadyDeletedException` ×4 | `AggregateAlreadyDeleted` case |
| `AggregateNotDeletedException` / `AggregatedNotAlreadyDeletedException` ×4 | `AggregateNotDeleted` case |
| `AggregateLockedException` ×4, `LockedException` | `AggregateLocked` case |
| `ValidationException` thrown by `SaveResult.EnsureValid()` | `AggregateInvalid` case |
| `GetAsync` returning `null` | `AggregateNotFound` case |

The ~25 duplicated provider exception files are **deleted**; providers translate their storage errors into the
core cases at the provider boundary.

## Consequences for existing API

**`SaveResult<TAggregate>` is replaced** by `Result<SaveOutcome<TAggregate>, AggregateSaveFailure>`, where
`SaveOutcome<TAggregate>` carries the aggregate and why no write occurred when none did
(`Written`, `NoChanges`, `AlreadyApplied` for an idempotent replay). This removes the `Saved`/`Skipped`
ambiguity and the implicit `bool` conversion.

> **Blast radius.** `bool saved = await store.SaveAsync(...)` was used throughout the samples, fixtures and the
> shared contract suites. Every one became an explicit result check, done in one pass.

**`LockHandlingMode` is deleted outright**, along with `EventStoreOperationContext.LockMode`. Removing the
throwing mode left one member, and a one-member enum is not a choice: a locked aggregate is always reported as
`AggregateLocked`.

**Delete and restore widen the save's failure.** Both append an event through the save path, so
`AggregateSaveFailure` is mapped into their own families — `AggregateDeleted` becomes
`AggregateAlreadyDeleted` for a delete, and a lost concurrency check now reaches the caller as
`ConcurrencyConflict` instead of being flattened into "no changes". Validation can reject either, so
`AggregateInvalid` joined both unions.

**`DeleteHandlingMode` shrinks to two members.** The throwing mode is gone; the remaining two decide whether a
deleted aggregate is an outcome or a failure:

```csharp
public enum DeleteHandlingMode
{
    ReturnsFailure = 0,   // default - reported as AggregateDeleted
    ReturnsAggregate,     // returned in its deleted state, as a success
}
```

`ReturnsNull` is renamed because nothing returns null any more, and `ReturnsAggregate` has to be a *success*
rather than "the case plus the state": the cases are non-generic core records, so none of them can carry a
`T`. `GetDeletedAsync` is the one caller that asks for `ReturnsAggregate`.

**`ExistsState` was already a typed outcome, not an error**, so it survived the migration unchanged. The
API cleanup then renamed it `AggregateExistence` and removed its implicit `bool` conversion, which was
flattening "exists in a deleted state" into "exists"; see the note below on `IsDeletedAsync`.

**Query APIs stay as they are.** A predicate that cannot be translated is a programming error, so
`IQueryableEventStoreCore<T>` keeps throwing. See the decision below.

## Tooling constraints

Two things the toolchain imposes, both verified against the code rather than assumed:

- **CSharpier cannot format a union.** Version 1.3.0 — the latest — reports *"Was not formatted due to
  syntax errors"* for any file containing a `union` declaration. Union declarations therefore live in
  dedicated files matched by `**/Failures/*Failure.cs` in `.csharpierignore`; the exclusion is removed when
  CSharpier supports the syntax. Everything else, including the case types, formats normally.
- **`RSG1006` is expected and suppressed in the core project.** The unions deliberately share leaf cases, so
  each shared case has its `AsFailure` helper generated for one union only. Using the wrong helper is a
  compile error, not a silent mis-binding, which is why the suppression is safe.

## Composition

Callers of several stores wrap the callee unions as cases rather than re-listing leaves, then widen with
`Bind`/`MapError`:

```csharp
[GenerateResult]
public readonly union PlaceOrderFailure(AggregateSaveFailure, AggregateReadFailure);
```

The Admin API maps results to HTTP in one place, so the mapping from `AdminAggregateNotFound` → 404 lives
there instead of in every endpoint.

> `Purview.Results.AspNetCore` is the intended home for this. The published version (1.0.0.1) keeps
> `ToHttpResult` and `ResultHttpExtensions` internal, exposing only the endpoint filter — which cannot be
> used by handlers that also return validation problems. Until a release exposes the mapper, `Admin.API`
> owns a single `AdminQueryFailureResults` table and the package is not referenced.

## Rollout

The conversion ran **one operation family at a time**, each ending with a green build: changing the whole
contract at once would have left the repository non-compiling across far more work than a single change
should span. Measured call sites: `SaveAsync` 419, `GetAsync` 412, `DeleteAsync` 134, `SaveResult` 179.

| # | Step | Status |
| --- | --- | --- |
| 1 | Case types and unions in core — additive | **Done** |
| 2 | Unify the duplicated provider exception types into core | **Done** — 36 definitions deleted, 3 of them never constructed |
| 3 | `DeleteAsync` / `RestoreAsync` through contract, facades, extensions, all five providers, snapshot stores and callers | **Done** |
| 4 | `GetAsync` / `GetAtAsync` / `GetOrCreateAsync` / `GetDeletedAsync` → `Result<T, AggregateReadFailure>` | **Done** — `DeleteHandlingMode.ThrowsException` removed with it |
| 5 | `SaveAsync` → `Result<SaveOutcome<T>, AggregateSaveFailure>`; retire `SaveResult<T>` | **Done** — `LockHandlingMode` removed with it |
| 6 | Admin query services, and map results to HTTP in the Admin API | **Done** — mapping is local to the Admin API; see the note below |
| 7 | `ConcurrencyRetry` retargeted from `IConcurrencyConflict` to the `ConcurrencyConflict` case; delete the transitional exception types | **Done** — result-aware overload added; four dead exception types deleted |
| 8 | Update `Guarantees-and-Limitations.md` and the provider guides | **Done** |

Steps 4 and 5 were the large ones. Steps 5 and 7 had to land together: the moment a provider stops throwing
`ConcurrencyException`, an exception-based retry loop silently stops retrying.

## Still outstanding

Nothing. `IsDeletedAsync` — the last read on the old model, returning `bool` and throwing for a missing
stream — was removed in the API cleanup. Its one distinct answer is now `AggregateExistence.IsDeleted`,
alongside `DoesExist`, on the result `ExistsAsync` already returned.

## Decisions

| Question | Decision |
| --- | --- |
| Query failures | `IQueryableEventStoreCore<T>` **keeps throwing**. A predicate that cannot be translated is a programming error, not an outcome a caller can act on, so no `QueryFailure` union is introduced. |
| `DeleteHandlingMode` | **Kept**, reduced to `ReturnsFailure` (default) and `ReturnsAggregate`. The throwing behaviour is removed. The original wording — "the case plus the deleted state" — turned out to be unimplementable, because the core cases are non-generic and cannot carry a `T`; `ReturnsAggregate` therefore returns a success. |
| `AggregateNotDeleted` in the read union | **Added.** `GetDeletedAsync` is a read that can fail because the aggregate is live, and it used to throw `AggregateNotDeletedException`. Widening the read union by one case beat minting a per-method union for it; the other reads never produce it. |
| `SaveOutcome` granularity | **Three outcomes**: `Written`, `NoChanges`, `AlreadyApplied`. An idempotent replay and an empty save are operationally different — the first proves a marker matched, the second that the caller had nothing to persist — and collapsing them would re-create the ambiguity this change exists to remove. |
