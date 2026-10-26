---
name: purview-zodsharp-core
description: >-
  Use when validating data with Purview.ZodSharp at runtime - picking the right package, building
  schemas with the Z factory, Validate/SafeParse/Parse, reading ValidationResult and ValidationError,
  composing with Transform/Refine/Pipe/Catch/Default, and how null handling and type coercion work.
license: MIT
metadata:
  product: Purview.ZodSharp
  repository: purview-dev/zodsharp
---

# Purview.ZodSharp core

A C# port of [Zod](https://github.com/colinhacks/zod): schemas you build fluently, validated without
exceptions, reporting machine-readable issues.

## Packages

```bash
dotnet add package Purview.ZodSharp                    # core, source generator, JSON Schema export
dotnet add package Purview.ZodSharp.SystemTextJson     # STJ deserialize-and-validate + schema import
dotnet add package Purview.ZodSharp.NewtonsoftJson     # Newtonsoft equivalent
dotnet add package Purview.ZodSharp.AspNetCore         # ProblemDetails + DI
```

JSON Schema **export** (`Z.ToJsonSchema`) is in the core package; **import** (`Z.FromJsonSchema`) comes
from whichever JSON integration you reference, so import its namespace
(`ZodSharp.JsonSchema.SystemTextJson` or `ZodSharp.JsonSchema.NewtonsoftJson`).

## First schema

```csharp
using ZodSharp;

var nameSchema = Z.String().Min(3).Max(50);
var result = nameSchema.Validate("John");

if (result.IsSuccess)
    Console.WriteLine(result.Value);
```

## Validate, SafeParse, Parse, ValidateAsync

| Member | Behaviour |
| --- | --- |
| `Validate(value)` | returns `ValidationResult<T>`; **never throws** |
| `SafeParse(value)` | alias of `Validate` |
| `Parse(value)` | returns the validated value, or throws `ZodException` (via `GetValueOrThrow()`) |
| `ValidateAsync(value, ct)` | `ValueTask` wrapper. The pipeline is synchronous; the token is observed before validation and throws `OperationCanceledException` if already cancelled. Genuinely async work only happens in the source generator's custom async validation. |

Reach for `Validate` by default and `Parse` only where a throw is the right control flow. Exceptions
for expected invalid input are expensive and lose the structured issue list unless you catch.

## `ValidationResult<T>`

A `readonly record struct` in `ZodSharp.Core`:

- `bool IsSuccess` — annotated `[MemberNotNullWhen(true, nameof(Value))]`, so the compiler knows
  `Value` is non-null inside the success branch.
- `T? Value` — valid only when `IsSuccess`.
- `ImmutableArray<ValidationError> Errors`.

Factories: `Success(value)`, `Failure(ValidationError)`, `Failure(ImmutableArray<ValidationError>)`,
`Failure(IEnumerable<ValidationError>)`, and `Merge(lhs, rhs)` — succeeds only if both succeed, and
concatenates the errors otherwise.

## `ValidationError`

A `readonly record struct` carrying machine-readable metadata, not just a message:

| Member | Meaning |
| --- | --- |
| `Code` | `invalid_type`, `too_small`, `too_big`, `invalid_union`, `missing_field`, `unrecognized_key`, `validation_failed`, `refinement_failed`, `transform_error`, … |
| `Message` | human-readable |
| `Path` | `ImmutableArray<string>` — `["user", "email"]`; array indexes are string segments, `"0"` |
| `Origin` | category for size issues: `"string"`, `"array"`, `"collection"` |
| `Minimum` / `Maximum` / `Inclusive` | the bound, for size issues |
| `Parameters` | `IReadOnlyDictionary<string, object?>`; a union failure carries every option error under `"errors"` |

Build one with
`ValidationError.Create(code, message, path, parameters, origin, minimum, maximum, inclusive)`.

`ZodException` carries `ImmutableArray<ValidationError> Errors`; its `ToString()` renders one line per
error as `"{joinedPath}: {Message} ({Code})"`.

## The `Z` factory

| Method | Returns |
| --- | --- |
| `Z.String()` / `Z.Number()` / `Z.Boolean()` / `Z.Null()` / `Z.Date()` / `Z.BigInt()` | the matching `Zod*` schema |
| `Z.Array<T>(elementSchema)` | `ZodArray<T>` |
| `Z.Optional<T>(schema)` (`T : class`) / `Z.Nullable<T>(schema)` (`T : struct`) | `ZodOptional<T>` / `ZodNullable<T>` |
| `Z.Object()` | `ZodObjectBuilder` |
| `Z.Union(params ...)` / `Z.Union<T1, T2>(...)` | `ZodUnion` / `ZodTypedUnion<T1, T2>` |
| `Z.DiscriminatedUnion(discriminator)` | `ZodDiscriminatedUnionBuilder` |
| `Z.Intersection<T>(left, right)` | `ZodIntersection<T>` |
| `Z.Literal<T>(value)` (`T : IEquatable<T>`) | `ZodLiteral<T>` |
| `Z.Lazy<T>(schemaGetter)` | `ZodLazy<T>` — for recursive schemas |
| `Z.Enum(params string[])` / `Z.Enum<TEnum>()` | `ZodEnum` / `ZodNativeEnum<TEnum>` |
| `Z.Record<TValue>(valueSchema)` | `ZodRecord<TValue>` |
| `Z.Tuple<T1, T2>(…)` / `Z.Tuple<T1, T2, T3>(…)` | `ZodTuple<…>` |
| `Z.ToJsonSchema<T>(schema, options?)` | `JsonSchemaDefinition` |

`Enum` and `Union` are overloaded — `Enum(params string[])` versus `Enum<TEnum>()`, and
`Union(params …)` versus `Union<T1, T2>(…)`.

```csharp
var userSchema = Z.Object()
    .Field("name", Z.String().Min(1))
    .Field("age", Z.Number().Min(0).Max(120).Int())
    .Field("email", Z.String().Email())
    .Build();
```

## The validation pipeline

Every schema derives from `ZodType<TOutput, TInput>` (`ZodSharp.Core`). Two phases:

1. **`ParseInternal`** — the schema's own type check and traversal: rejecting `null` where not
   allowed, coercing, walking objects/arrays/tuples/unions, producing structured failures.
2. **Rules** — on success, the accumulated `IValidationRule<TOutput>` structs run. A failing rule emits
   a `ValidationError` with its Zod-compatible code.

Rules are `readonly record struct`s implementing `IValidationRule<T>` (`bool IsValid(in T value)`,
`string GetErrorMessage(in T value)`) — zero allocation. Each exposes `public const string ErrorCode`
and `MessageFormat`, so a test asserts against the rule rather than a duplicated literal. (A rule
without them is `ZODSGEN042`.)

`ZodString` also exposes `ValidateSpan(ReadOnlySpan<char>)` and `IsValidSpan(value, out errors)` for
allocation-free string validation; string rules implementing `IStringValidationRule` participate.

## Composition

`ZodType` composes by **wrapping**, never mutating. Each of these returns a new schema:

| Call | Produces |
| --- | --- |
| `.Transform<TNew>(func)` | `ZodTransform<TOutput, TNew>` |
| `.Refine(predicate, message?)` | `ZodRefinement<TOutput>` |
| `.SuperRefine(Action<RefineCtx<TOutput>>)` | `ZodSuperRefinement<TOutput>` |
| `.Pipe<TTarget>(schema)` | `ZodPipe<TOutput, TTarget>` |
| `.Catch(value \| func)` | `ZodCatch<TOutput>` |
| `.Prefault(value)` / `.Default(value)` | `ZodPrefault<TOutput>` / `ZodDefault<TOutput>` |
| `.And(schema)` / `.Or<TOther>(schema)` | `ZodIntersection<TOutput>` / `ZodTypedUnion<TOutput, TOther>` |
| `.Describe("…")` | sets `Description` |

Because composition lives on the base type, every schema chains all of them.

## Null and optionality

Schemas report optionality through the internal `IOptionalSchema`:

- `IsOptional` — true for `ZodOptional`, `ZodNullable`, `ZodDefault`, `ZodPrefault`.
- `ProvidesValueOnMissing` — true for `ZodDefault` and `ZodPrefault`. Object fields route a missing
  value through `Validate(null!)` so the produced value is injected.
- `IAcceptsNull.ValidateNull()` is implemented by the nullable/optional/default/prefault schemas and
  `ZodNull`; object-field and union wrappers route `null` to it rather than failing coercion.

## Type coercion

Object fields and union options wrap typed schemas, so boxed values out of a
`Dictionary<string, object?>` validate:

- `null` routes to `IAcceptsNull.ValidateNull()` where supported.
- An exactly-typed value reuses the original box.
- Numeric coercion goes through `IConvertible` in the **invariant** culture, so a boxed `long`
  satisfies a `Z.Number()` field.
- A non-nullable value type rejects `null`; anything else falls through to failure.

## Related skills

`purview-zodsharp-source-generator` for compile-time `[ZodSchema]` validators (which is what you want
for a DTO), `purview-zodsharp-custom-rules` for writing rules and rule attributes,
`purview-zodsharp-aspnetcore` for ProblemDetails and DI, and `purview-zodsharp-diagnostics` for the
`ZODSGEN`/`ZODSASP` catalogue.
