# Migrating ad-hoc failure signalling to an error union

A playbook for replacing thrown domain exceptions, `bool`/`out` pairs, sentinel return values and
error-code enums with a C# 15 `union` of case types returned as `Result<TValue, TUnion>`.

## Before you start, collect

- The file(s) being migrated, and the operation family they belong to — a union is per family, not
  per method, so methods can come and go without changing the error contract.
- A name for the union (for example `TenantError`).
- The failure modes to model as cases, or the decision to infer them from the current code.
- Whether genuinely exceptional exits stay throwing. They normally should: a lost connection or a
  violated invariant is not a domain outcome.

## Requirements

1. Declare one `readonly record struct` per failure mode, carrying the payload a caller needs to
   react, and declare the union once:

   ```csharp
   [GenerateResult]
   public readonly union TenantError(TenantNotFound, TenantDisabled, TenantAlreadyExists);
   ```

2. Change the affected signatures to `Result<TValue, TenantError>` — or the value-less
   `Result<TenantError>` when the member has nothing to return on success — and produce failures with
   `TenantError.Failure<TValue>(case)` / `TenantError.Failure(case)`, or the per-case
   `new Case(...).AsFailure<TValue>()` / `AsFailure()` helpers.
3. Leave genuinely exceptional exits throwing.
4. Do **not** attempt implicit conversions from a case to a result, and do not add operators,
   extension conversions or wrapper types: `CS0715`, `CS0556`, `CS9282`, `CS0246` and `CS0029` each
   block that route. Use the generated factory or helper, or `(TenantError)new TenantNotFound(id)`
   when a cast reads better. Prefer the union-receiver factory when a leaf case type is shared with
   another union (`RSG1006`), because it names the union.
5. Remove the replaced mechanism — thrown domain exceptions, error enums, `out` error parameters,
   sentinel values — rather than leaving both paths in place.
6. Keep the union free of transport concerns: no `IResult`, no status codes, no HTTP types.
7. One case type per meaning. A case carrying a `string Reason` or an `int Code` invites string or
   int matching and defeats the point.
8. Compose unions rather than repeating leaf cases. A case type that is itself a union is an
   *included union*: the including union's factory also covers its cases and builds the nested value,
   and sharing a union-typed case across unions is not reported. This is how to avoid `RSG1006`,
   which applies only to a shared **leaf** case.

## Migration strategy

- Inventory the failure modes and the call sites of each affected member.
- Introduce the cases and the union before changing signatures, so each edit compiles in isolation.
- Update call sites to `Match` / `Map` / `Bind` where a value has to come out of the result, rather
  than `if (IsSuccess)`.
- Update or add tests: one per case, asserting the case the caller receives.
- Map the union once in the host — see the `purview-results-http-mapping` skill — instead of deciding
  status codes in the domain.

## Verification

- Build the solution and confirm no `RSG1000`–`RSG1008` diagnostics.
- Run the affected tests.
- Confirm no remaining `throw` for an outcome the caller is expected to handle.
- Confirm the union appears in exactly one mapper, not scattered through handlers.
- Confirm no method can fall off the end of its body: `default` is a bug, not a failure.
- Reflect public API changes in the package documentation.

## Report

When the migration is done, state: the files changed, the union and its cases, the failure modes that
stayed exceptional and why, and the verification performed.
