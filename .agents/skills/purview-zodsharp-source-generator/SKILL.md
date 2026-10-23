---
name: purview-zodsharp-source-generator
description: >-
  Use when generating a compile-time validator with Purview.ZodSharp's [ZodSchema] attribute - which
  types it emits, the attribute options, DataAnnotations coverage, the OnZodValidate refinement hook,
  async custom validation, automatic enum validation, IValidateOptions support, and the MSBuild
  switches that control it.
license: MIT
metadata:
  product: Purview.ZodSharp
  repository: purview-dev/zodsharp
---

# The `[ZodSchema]` source generator

Mark a class, struct or record with `[ZodSchema]` and a static, zero-allocation validator is emitted
at compile time — no reflection at runtime. The attribute itself is generated into the `ZodSharp`
namespace by `Purview.ZodSharp.SourceGenerators`, so nothing beyond `Purview.ZodSharp` is needed.

```csharp
using System.ComponentModel.DataAnnotations;
using ZodSharp;

[ZodSchema]
public class User
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 120)]
    public int Age { get; set; }

    [EmailAddress]
    public string? Email { get; set; }
}

var result = UserSchema.Validate(user);
var validated = UserSchema.Parse(user);   // throws ZodException on failure
```

## What gets emitted

| Artifact | Shape |
| --- | --- |
| `{TypeName}Schema` | static partial class — the validator. Accessibility mirrors the target. Holds `Validate`, `Parse`, `BuildJsonSchema`, and (with composition on) `ApplyAnd`, `ApplyOr`, `ApplyRefine`. Renamed by `SchemaName`. |
| `{TypeName}SchemaValidator` | `partial class : IZodSchemaValidator<{TypeName}>, IJsonSchemaProvider` — the DI adapter, with `Validate`/`ValidateAsync`/`BuildJsonSchema`. Primary schema only; `{SchemaName}Validator` when renamed. |
| `{TypeName}Validator` | `sealed partial class : IValidateOptions<{TypeName}>` — only when `IValidateOptions` support is on, and the target is a class. |
| `[assembly: ZodSchemaGenerated(typeof({TypeName}))]` | the marker `IZodSchemaFactory` assembly scanning consumes. Primary, non-nested schemas only. |

`BuildJsonSchema` is built from the same metadata as `Validate`, so a generated schema exports through
`Z.ToJsonSchema` without rebuilding its rules by hand.

```csharp
var adult  = UserSchema.ApplyRefine(user, u => u.Age >= 18, "Must be adult");
var both   = UserSchema.ApplyAnd(user, u => u.Name.Length > 5, "Name too short");
var either = UserSchema.ApplyOr(user, u => u.Age < 18, "Must be an adult, or a minor with consent");
```

## Attribute options

| Property | Default | Purpose |
| --- | --- | --- |
| `SchemaName` | `null` | Renames `{TypeName}Schema`; the DI adapter becomes `{SchemaName}Validator`. |
| `GenerateValidateMethod` | `true` | `false` omits `Validate` **and everything that depends on it**. |
| `GenerateParseMethod` | `true` | `false` omits `Parse`. |
| `EnableComposition` | `true` | Emits `ApplyAnd`/`ApplyOr`/`ApplyRefine`. |
| `CustomValidationMethodName` | `null` | Names the async custom validation method (default lookup `CustomValidationAsync`). Mutually exclusive with `OnZodValidate`. |
| `GenerateIValidateOptions` | `false` | Forces `IValidateOptions<T>` generation. |
| `SuppressIValidateOptions` | `false` | Opts out even when auto-detection would enable it. |
| `ValidateEnumValues` | `true` | `false` skips automatic enum validation. |
| `GenerateJsonSchema` | `true` | `false` omits `BuildJsonSchema` and `IJsonSchemaProvider`. A type referenced by an exported type is `ZODSGEN045`. |
| `AllowNulls` | unset | Whether this type's collection properties may contain `null` elements. Unset defers to the assembly default. |

`Parse`, the composition methods, the `IZodSchemaValidator` adapter and the `IValidateOptions`
validator all depend on `Validate` — `GenerateValidateMethod = false` removes them together.

## What is validated

- Properties must be public, non-static, non-indexer.
- A property gets validation when it carries **any** DataAnnotations attribute, when its type is a
  source-defined complex type with a nested schema, or when its type is an enum.
- Nested complex types are discovered recursively and get their own `{TypeName}Schema`, **even
  without** their own `[ZodSchema]`.
- Nullable properties are null-guarded before value-set/type validation; a nullable target rejects
  `null` with `invalid_type`.
- Collection elements reject `null` by default: the error is `invalid_type` at the element's own path
  (`["Items", 0]`). Control it with `[CollectionBehavior(AllowNulls = ...)]` on the property, a `T?`
  element annotation (implies allow), `[ZodSchema(AllowNulls = ...)]` on the containing type,
  `[assembly: ZodSharpSchemaDefaults(AllowNulls = ...)]`, then the `ZodSharpAllowNulls` MSBuild
  property. The most specific statement wins; an unset level defers to the next. An explicit
  `AllowNulls = true` exports a `null` branch on the collection's `items` schema.
- **Dictionaries are maps, not arrays.** A property whose type implements `IDictionary<TKey, TValue>`
  or `IReadOnlyDictionary<TKey, TValue>` (for example `Dictionary`, `SortedDictionary`,
  `ConcurrentDictionary`, `ImmutableDictionary`) validates every **value** against the value type's
  nested schema and reports a failure at `["Items", "key"]`. Keys are used only in error paths, and
  `AllowNulls` governs a null value (`Dictionary<string, Child>` rejects it by default; `Child?` or
  `[CollectionBehavior(AllowNulls = true)]` accepts it). A dictionary exports as
  `{ "type": "object", "additionalProperties": … }` and `[Length]`/`[MinLength]`/`[MaxLength]` map to
  `minProperties`/`maxProperties` — never `z.array`/`items`.
- Classes, structs and records are supported. Structs get no `IValidateOptions` (`ZODSGEN028` if
  requested).

DataAnnotations coverage, positional-record attribute targets, structured size issues and the
`[Range]` shapes are in [references/dataannotations.md](references/dataannotations.md).

## The `OnZodValidate` refinement hook

Rules DataAnnotations cannot express are written as a **generator-declared partial method**. The
generator emits the declaration, so the IDE offers the implementation with the right signature and
nothing is resolved by naming convention:

```csharp
[ZodSchema]
public partial class Order
{
    public decimal Total { get; set; }

    partial void OnZodValidate(RefineCtx<Order> context)
    {
        if (context.Value.Total < 0)
            context.AddIssue("invalid_range", "Total cannot be negative", [nameof(Total)]);
    }
}
```

- The type **and every containing type** must be `partial` (`ZODSGEN034`). That is the only shape
  requirement the hook adds.
- The signature must be `partial void OnZodValidate(RefineCtx<T> context)`, `T` being the model
  (`ZODSGEN035`). The parameter is a plain by-value `RefineCtx<T>`.
- The hook runs for **every** entry point — `Validate`, `Parse`, the `IZodSchemaValidator` adapter,
  `IValidateOptions`, any factory that validates through the schema — so a rule written here behaves
  exactly like an attribute rule.
- A type that does not implement it allocates nothing: the generated `Validate` only builds a
  `RefineCtx<T>` and calls the hook when a body exists.
- The retired `IEnumerable<ValidationError> Validate()` contract is no longer bound, and is reported
  as `ZODSGEN036`.

Alongside the hook the generator emits one `internal static`
`InvokeZodRefinementHook(T value, RefineCtx<T> context)` bridge on the target type. A classic partial
method is private and `{Type}Schema` is a different type, so the bridge is what lets `Validate` reach
the hook while keeping the hook optional. **It is not part of your API; never implement it by hand.**

## Async custom validation

Mutually exclusive with `OnZodValidate` — declaring both is `ZODSGEN029`.

```csharp
public partial class UserSchemaValidator
{
    public async ValueTask<ValidationResult<User>> CustomValidationAsync(User value, CancellationToken ct)
    {
        await Task.Delay(1, ct);
        return ValidationResult<User>.Success(value);
    }
}
```

Signature `ValueTask<ValidationResult<T>> Name(T value, CancellationToken ct)`. The default lookup
name is `CustomValidationAsync` unless `CustomValidationMethodName` overrides it. A method on the
**model type** must be `static`; one on the generated `{TypeName}SchemaValidator` partial may be an
instance method. The generated `ValidateAsync` runs the synchronous `Validate`, awaits the custom
method, and merges the error sets.

## Automatic enum validation

Every non-`[Flags]` enum property is validated automatically: a value that is not a defined member is
rejected with `invalid_enum_value`, emitted as an `EnumRule<TEnum>`.

```csharp
public enum ExampleEnum
{
    [ZodIgnore]          // never a valid value, for every property of this type
    Unspecified,
    AValidValue,
    AnotherValidValue,
}

[ZodSchema]
public class Model
{
    public ExampleEnum Status { get; set; }

    [DeniedValues(ExampleEnum.AnotherValidValue)]   // excluded for this property only
    public ExampleEnum SecondaryStatus { get; set; }
}
```

It is skipped when the enum is `[Flags]` (a combination is valid without being a defined member),
when the property declares an explicit `[AllowedValues]` allow-list (which governs instead), or with
`[ZodSchema(ValidateEnumValues = false)]`. A nullable enum is validated only when not `null`.

## `IValidateOptions` support

Enabled by `GenerateIValidateOptions = true`, by auto-detection (unset, target is not a value type,
and the type name ends with a configured suffix — `Options` or `Settings` by default), or by MSBuild.

| MSBuild property | Default | Behaviour |
| --- | --- | --- |
| `DisableZodSharpSourceGenerator` | unset | truthy disables the generator entirely |
| `ZodSharpAutoGenerateOptionsValidators` | `true` | only an explicit `false` disables auto-detection |
| `ZodSharpAutoGenerateOptionsValidatorSuffixes` | `Options;Settings` | semicolon/comma-separated |
| `ZodSharpGenerateJsonSchemas` | `true` | explicit `false` omits `BuildJsonSchema` and `IJsonSchemaProvider` assembly-wide |
| `ZodSharpEnumSerialization` | `Number` | `String` exports enums as member names, matching a global `JsonStringEnumConverter` |
| `ZodSharpEnumNamingPolicy` | `Unchanged` | `CamelCase` applies `JsonNamingPolicy.CamelCase` to string-serialized enum names |
| `ZodSharpAllowNulls` | `false` | assembly-wide default for whether collections may contain `null` elements; explicit `true` accepts them |
| `ZodSharpDateTimeOffsetSerialization` | `PreserveOffset` | `Utc` narrows `[NonSentinel] DateTimeOffset` to a zero-offset wire contract; pair with the integration package's `UtcDateTimeOffsetConverter`. Both modes export an exact sentinel pattern |

`DateTimeOffset` precedence: `[DateTimeOffsetBehavior]` on the property/parameter, then on the type, then
`[assembly: ZodSharpSchemaDefaults(DateTimeOffsetSerialization = ...)]`, then the MSBuild property, then
`PreserveOffset`. The attribute shapes the schema only; `Utc` requires a converter that also rejects non-UTC
input on read.

## Value objects

A `Purview.ValueObjects` scalar marked `[Scalar]` can carry `[ZodSchema]` on the same type. The
generated validator validates the scalar **as a unit** and reports an empty path, and a rule written
against the underlying value is adapted automatically.

## Related skills

`purview-zodsharp-custom-rules` for `[ZodRule]` and the built-in rule attributes,
`purview-zodsharp-diagnostics` for the `ZODSGEN` catalogue, and `purview-zodsharp-core` for the
runtime API the generated code reports through.
