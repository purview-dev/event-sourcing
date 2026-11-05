---
name: purview-results-core
description: "Use when writing or reviewing code that returns Purview.Results Result<TValue, TError> or value-less Result<TError> values — choosing between exceptions and results, creating, reading, or transforming a result, and reasoning about the uninitialized default state."
---

# Purview.Results core

Use this skill whenever a method returns `Result<TValue, TError>` or the value-less `Result<TError>` from the
`Purview.Results` package.

## The three states

`Result<TValue, TError>` is a `readonly record struct`, so `default` is a real, observable state.

| State | `IsInitialized` | `IsSuccess` | `IsFailure` | `Value` | `Error` |
| --- | --- | --- | --- | --- | --- |
| Success | `true` | `true` | `false` | the value | throws |
| Failure | `true` | `false` | `true` | throws | the error |
| Uninitialized (`default`) | `false` | `false` | `false` | throws | throws |

`Value` and `Error` throw `InvalidOperationException` when the result is in the wrong state, and `Match`, `Map`,
`Bind` and `MapError` throw `"The result is uninitialized."` for `default`. **An uninitialized result is a bug,
never a success and never a failure** — if a method can fail to produce a result, return a failure case instead
of falling off the end of the method body.

## Creating results

```csharp
// Named factories
Result<Tenant, TenantError>.Success(tenant);
Result<Tenant, TenantError>.Failure(new TenantNotFound(id));

// Type-inferring factories
Result.Success<Tenant, TenantError>(tenant);
Result.Failure<Tenant, TenantError>(new TenantNotFound(id));

// Implicit conversions (one hop only)
Result<Tenant, TenantError> ok = tenant;
TenantError error = new TenantNotFound(id);          // case -> union is one hop
Result<Tenant, TenantError> failed = error;          // union -> result is the next hop

// Union-receiver factory (generated for [GenerateResult] unions; shared-case safe)
TenantError.Failure(new TenantNotFound(id));
TenantError.Failure<Tenant>(new TenantNotFound(id));
TenantError.Success();
TenantError.Success(tenant);
```

The implicit conversions accept `TValue` or `TError` directly. They do **not** compose: a union *case* cannot
convert to the result in one hop, which is why the source generator exists (see the
`purview-results-union-errors` skill).

## Reading and transforming

| Member | Purpose |
| --- | --- |
| `Match(success, failure)` | Fold both states into one value |
| `Map(map)` | Transform the successful value, preserving the error |
| `Bind(bind)` | Chain an operation that can fail, preserving the error type |
| `Bind(bind, mapError)` | Chain an operation that fails with a different error type, widening the existing error |
| `MapError(map)` | Transform the error, preserving the value |
| `TryGetValue(out value)` | Probe without throwing: `true` only for a success |
| `TryGetError(out error)` | Probe without throwing: `true` only for a failure |
| `GetValueOrDefault()` / `(fallback)` / `(factory)` | Value, or a default/fallback — throws for `default` |
| `GetErrorOrDefault(fallback)` | Error, or a fallback — throws for `default` |
| `OrElse(fallback)` | This result when successful, otherwise another result |
| `Switch(success, failure)` | Side effects per state; returns `void` |
| `Tap(success)` / `TapError(failure)` | Side effects that return the result, so calls can be chained |
| `Ensure(predicate, errorFactory)` | Turn a value that fails a predicate into a failure |
| `MapAsync(map)` / `BindAsync(bind)` | Asynchronous `Map`/`Bind` |
| `BindAsync(bind, mapError)` | Asynchronous widening `Bind` |
| `Throw()` | Return the value, or throw `ResultException<TError>` carrying the error (escape hatch) |

Prefer `Match`/`Map`/`Bind` over `if (result.IsSuccess)` when a value has to come out of the result: they keep
the failure path explicit and cannot silently leak a `default`.

`TryGetValue`/`TryGetError` are the only members that never throw; the rest throw for `default` so a forgotten
initialization surfaces immediately.

## Value-less results

When an operation has nothing to return on success — a command, a validation-only step — use `Result<TError>`
instead of inventing a meaningless value (a `bool`, a `Unit`). It has the same three states and the same
throw-on-misuse contract, but a success holds the `Success` marker and its success callbacks take no argument:

```csharp
Result<TenantError> DeleteTenant(TenantId tenantId) =>
    _tenants.Remove(tenantId)
        ? Result<TenantError>.Success()
        : new TenantNotFound(tenantId).AsFailure();

deleted.Match(() => "deleted", error => $"could not delete: {error}");
deleted.Map(() => 1);                                  // attach a value: Result<int, TenantError>
deleted.Bind(() => GetTenant(tenantId));               // continue with another operation
deleted.Ensure(() => _tenants.Count > 0, () => new TenantError());
```

- `Result<TError>.Success()` / `.Failure(error)`, `Result.Success<TError>()` / `Result.Failure<TError>(error)`,
  and the implicit conversions from `Success` and `TError` create a unit result.
- `Match`, `Switch`, `Tap` and `Ensure` take a **no-argument** success delegate; `Map` produces a value from a
  success that had none; `Bind` continues with either another unit operation or a value-producing one.
- `Error` throws in the wrong state; `Throw()` returns the `Success` marker or throws `ResultException<TError>`;
  `ToString()` is `Success` / `Failure(error)` / `Uninitialized`.
- `Result<TValue, TError>.DiscardValue()` drops a value result's value and keeps the error, so a value result can
  join a unit-result chain.

Use `Result<TError>` when the failure is the only interesting outcome; use `Result<TValue, TError>` when the
caller needs the produced value.

## Infrastructural view

`IResultValue` is the non-generic, read-only view (`IsInitialized`, `IsSuccess`, `SuccessValue`, `ErrorValue`)
for code that cannot be generic over the value and error types — an endpoint filter or a logging middleware, for
example. Its accessors never throw: the one that does not describe the current state returns `null`, and a
successful unit `Result<TError>` reports the `Success` marker as its `SuccessValue`. Use it in infrastructure, and
the generic type in domain code.

## When to use a result

**Use it** for expected, domain-level outcomes the caller must handle: not found, invalid, conflict, disabled,
already exists. These are values with meaning, and making them explicit keeps the happy path readable.

**Throw instead** for programming errors and unrecoverable infrastructure faults (a lost connection, a violated
invariant, a missing configuration key). Wrapping those in a result forces every caller to branch on something
it cannot act on.

Rules of thumb:

1. If the caller is expected to *do* something different per outcome, the outcome is a result value.
2. If the caller can only log and rethrow, it is an exception.
3. Never use `Result<bool, TError>` where the failure is the only interesting outcome — use the value-less
   `Result<TError>` instead, or model the outcomes as cases.
4. Convert at the boundaries: a controller or endpoint handler turns the result into a response, an application
   service returns it, and repositories return it rather than throwing on "not found".

## Related skills

- `purview-results-union-errors` — modelling the error type as a union and generating the failure helpers.
- `purview-results-http-mapping` — turning results into ASP.NET Core responses.
- `purview-results-zodsharp-validation` — folding ZodSharp validation outcomes into results.
