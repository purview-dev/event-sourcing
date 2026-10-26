---
name: purview-results-zodsharp-validation
description: "Use when ZodSharp validation has to feed Purview.Results — converting ValidationResult<T> with ToResult or the value-discarding ToUnitResult, carrying ValidationError values on a union case, implementing IValidationErrorCarrier, or deciding what a rejected input becomes in the result pipeline."
---

# Folding ZodSharp validation into results

Use this skill when a codebase validates with the `Purview.ZodSharp` package and returns
`Purview.Results` values, and a rejected input has to become an ordinary failure rather than an exception.

## The conversion

```csharp
using Purview.Results;
using Purview.Results.ZodSharp;

return RepositoryReconciliationResultSchema
    .Validate(reconciliationResult)
    .ToResult<RepositoryReconciliationResult, ReconciliationError>(errors =>
        new ReconciliationResultInvalid(reconciliationResult, errors)   // a union case
    );
```

`ToResult<TValue, TError>(onFailure)`:

- returns a **success carrying the validated value** when validation passed;
- returns a **failure carrying the created error** when it failed;
- invokes `onFailure` only for a failure, so a successful validation allocates no error;
- takes the error type explicitly, and it must be named when it is a union, because a union case does not carry
  the union type that contains it.

ZodSharp validation never throws: `Validate` returns the value on success and every `ValidationError` on
failure. Keep it that way — map the outcome, do not re-throw it.

## When the validated value is not the method's success type

`ToResult` can only produce a result whose success type is the validated type. When the surrounding method
succeeds with something else (a repository result, an aggregate, a summary), produce the failure with the
generated helper instead:

```csharp
var validated = ProviderConnectionIdSchema.Validate(providerConnectionId);

if (!validated.IsSuccess)
    return new ProviderConnectionInvalid(providerConnectionId, validated.Errors)
        .AsFailure<RepositoryReconciliationResult>();

return await ReconcileCoreAsync(validated.Value, repositories, cancellationToken);
```

This keeps the failure case carrying the errors while the success type stays whatever the method actually
produces.

## When the method has no success value

When the method reports only why an input was rejected, `ToUnitResult` discards the validated value and returns a
value-less `Result<TError>`. It still names the validated value type, because a union case does not carry its
union:

```csharp
Result<TenantError> Validate(TenantInput input) =>
    TenantInputSchema
        .Validate(input)
        .ToUnitResult<TenantInput, TenantError>(errors => new TenantInputInvalid(input, errors));
```

Use `ToResult` when the success carries the validated value, `ToUnitResult` when it carries nothing, and the
generated `AsFailure<TValue>()` when the success carries something else.

## Async validation

`ValidateAsync` returns a `ValueTask<ValidationResult<T>>`, and the async counterparts fold it in the same way:

```csharp
async Task<Result<TenantError>> Validate(TenantInput input, CancellationToken cancellationToken) =>
    await TenantInputSchemaValidator
        .ValidateAsync(input, cancellationToken)
        .ToUnitResultAsync<TenantInput, TenantError>(errors => new TenantInputInvalid(input, errors));
```

- `ToResultAsync` carries the validated value on success; `ToUnitResultAsync` discards it.
- The error type is named explicitly for the same reason as the synchronous forms.
- Do not block on `ValidateAsync` to reuse the synchronous methods — that reintroduces the sync-over-async the
  bridge exists to avoid.

## Carrying the errors

```csharp
public readonly record struct ProviderConnectionInvalid(
    ProviderConnectionId ProviderConnectionId,
    ImmutableArray<ValidationError> ValidationErrors
) : IValidationErrorCarrier;
```

Implement `IValidationErrorCarrier` on the case (or error) that carries ZodSharp `ValidationError` values.
`ValidationErrors` preserves each error's code, origin, category, path and parameters, so an HTTP layer can render
a validation problem **without knowing the error type** — which is exactly how
`Purview.Results.ZodSharp.AspNetCore` maps it (see the `purview-results-zodsharp-problems` skill).

Guidance:

- **Keep the errors on the case**, never flatten them into a message string; the structured form is what clients
  and problem responses need.
- **One carrier per rejection**, holding the whole `ImmutableArray<ValidationError>` for that input.
- Implementing the interface (rather than a helper that extracts errors) lets the HTTP mapping stay generic.

## Why there is no factory that returns a result

`ToResult` takes `Func<ImmutableArray<ValidationError>, TError>` on purpose. A lambda that returns
`Result<TValue, TError>` would bind to that parameter — C# prefers `Func<…, TError>` — and silently nest the
results. Naming the error type explicitly keeps the intent unambiguous.

## Checklist

1. Validation outcomes are mapped, never thrown.
2. The error type is named explicitly (`ToResult<TValue, TUnion>(…)` or `ToUnitResult<TValue, TUnion>(…)`, and
   the async counterparts for a `ValidateAsync` outcome).
3. The case that carries the errors implements `IValidationErrorCarrier`.
4. A method whose success type differs uses `AsFailure<TValue>()`, or `AsFailure()` for a unit result, with the
   validation errors.
5. The host maps the carrier to a validation problem once (see `purview-results-zodsharp-problems`).
