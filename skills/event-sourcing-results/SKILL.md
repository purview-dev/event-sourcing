---
name: event-sourcing-results
description: "Use when calling or handling the result of a Purview.EventSourcing store operation - GetAsync, SaveAsync, DeleteAsync and RestoreAsync return Result values rather than null, bool or exceptions. Covers the failure cases, SaveOutcome, which failures are results versus exceptions, and retrying a concurrency conflict."
license: MIT
metadata:
  package: Purview.EventSourcing
  category: architecture
---

# Handling event-store results

The store returns outcomes a caller can act on as **result values**, and reserves exceptions for
faults a caller cannot. Nothing returns `null`, and nothing returns a bare `bool`.

> The `Result<TValue, TError>` type and its union helpers come from `Purview.Results`. For the
> general mechanics of composing results, prefer the `purview-results-core` and
> `purview-results-union-errors` skills; this skill covers the event store's own vocabulary.

## The principle

> A failure a correct caller can anticipate and act on is a **result case**.
> A failure meaning the system, the configuration or the calling code is wrong is an **exception**.

Cancellation is always `OperationCanceledException`, as everywhere else in .NET.

## Reads

`GetAsync`, `GetAtAsync`, `GetOrCreateAsync` and `GetDeletedAsync` return
`Result<T, AggregateReadFailure>`.

```csharp
var result = await store.GetAsync<OrderAggregate>(orderId, cancellationToken);
if (result.IsFailure)
{
    return result.Error switch
    {
        AggregateNotFound => Results.NotFound(),
        AggregateDeleted => Results.Problem(statusCode: 410),
        _ => Results.Problem(),
    };
}

var order = result.Value;
```

`AggregateReadFailure` carries `AggregateNotFound`, `AggregateDeleted`, `AggregateNotDeleted` and
`AggregateLocked`. **"Absent" and "deleted" are different answers** - the previous `T?` return could
not distinguish them.

## Writes

`SaveAsync` returns `Result<SaveOutcome<T>, AggregateSaveFailure>`. A success that wrote nothing is
still a success:

| `SaveOutcomeKind` | Meaning |
| --- | --- |
| `Written` | Events were appended |
| `NoChanges` | The aggregate had nothing to persist |
| `AlreadyApplied` | An idempotency marker matched an earlier attempt |

```csharp
var saved = await store.SaveAsync(order, cancellationToken);
if (saved.IsFailure)
{
    if (saved.Error is AggregateInvalid invalid)
        return Results.ValidationProblem(invalid.ValidationResult.Failures.ToProblem());

    return Results.Conflict();
}
```

`AggregateSaveFailure` carries `ConcurrencyConflict`, `AggregateDeleted`, `AggregateLocked` and
`AggregateInvalid`. `DeleteAsync` and `RestoreAsync` return their own unions, which add
`AggregateAlreadyDeleted` and `AggregateNotDeleted` respectively.

**Do not treat a failure as "nothing happened".** `NoChanges` is a success; `ConcurrencyConflict` is
a failure. Collapsing the two is the ambiguity this model exists to remove.

## Constructing failures

A case shared by more than one union has its per-case `AsFailure` helper generated once, so name the
union explicitly:

```csharp
// Correct - names the union.
return AggregateSaveFailure.Failure<SaveOutcome<Order>>(new AggregateLocked(type, id));

// Risky for a shared case - can bind to another union's helper.
return new AggregateLocked(type, id).AsFailure<SaveOutcome<Order>>();
```

## Concurrency retries

`ConcurrencyConflict` is a result, not an exception, so an exception-based retry loop will not see
it. Use the result-aware overload:

```csharp
var saved = await ConcurrencyRetry.ExecuteAsync(
    async ct =>
    {
        var current = await store.GetAsync<OrderAggregate>(orderId, ct);
        current.Value.Confirm();
        return await store.SaveAsync(current.Value, ct);
    },
    cancellationToken: cancellationToken
);
```

Re-read the aggregate inside the retry. Retrying with the stale instance conflicts again forever.

## Inside a transaction

`TransactionAggregateResult` exposes both. `Failure` carries the save failure a caller can act on;
`Error` carries only genuine exceptions. A `Saved == false` with neither set would be an
unexplained failure, which is why both exist:

```csharp
var conflicted = transactionResult.Results.Any(r => r.Failure is ConcurrencyConflict);
```

## What still throws

| Exception | Why |
| --- | --- |
| `CommitException` | Storage refused the write - infrastructure, not an outcome |
| `AggregateStreamVersionMissingException` | The stream is corrupt; no caller can continue |
| `MissingAggregateIdException`, `IdAlreadySetException` | Programming errors in aggregate construction |
| `UnregisteredEventException` | The aggregate never registered the event - a wiring defect |
| `EventStoreTransactionGuaranteeException` | A required guarantee the provider cannot honour |
| `TenantScopeNotSupportedException` | The provider cannot enforce the requested tenant scope |

Queryable stores also keep throwing: a predicate that cannot be translated is a programming error,
not an outcome.

## Checklist

1. No call site treats a read as nullable or a save as `bool`.
2. `AggregateNotFound` and `AggregateDeleted` are handled distinctly where it matters.
3. `NoChanges` and `AlreadyApplied` are treated as success.
4. Concurrency retries use the result-aware overload and re-read inside the loop.
5. Transactional callers inspect `Failure`, not just `Saved`.
