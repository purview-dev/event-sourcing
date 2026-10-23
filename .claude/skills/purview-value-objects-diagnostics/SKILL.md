---
name: purview-value-objects-diagnostics
description: >-
  Use when a Purview.ValueObjects build reports a VO1xxx diagnostic, when deciding whether to
  suppress one, or when adding a new analyzer rule to the value-objects source generator and its
  AnalyzerReleases tracking files.
metadata:
  product: Purview.ValueObjects
  repository: purview-dev/value-objects
---

# Purview.ValueObjects diagnostics

`Purview.ValueObjects` ships a Roslyn analyzer and source generator that report the `VO1xxx` rules
below. Every rule is enabled by default.

Suppress a warning the usual ways — `#pragma warning disable VO1006`, a `NoWarn` entry, or
`dotnet_diagnostic.VO1006.severity = none` in `.editorconfig`. **Errors cannot be downgraded**: they
mark declaration shapes the generator cannot emit code for at all, so silencing one only moves the
failure to the missing member.

## Core shape rules

| Rule | Severity | Reported when | Fix |
| --- | --- | --- | --- |
| `VO1001` | Error | A `[Scalar]` or `[ValueObject]` type is not declared `partial`. | Add `partial`. The code fix in `Purview.ValueObjects.SourceGenerator.Refactorings` does it for you. |
| `VO1002` | Error | The value object is nested inside another type. | Move it to its own top-level type; nesting is not supported. |
| `VO1003` | Error | The value object is generic. | Remove the type parameters, or declare one concrete value object per closed type. |
| `VO1004` | Error | A `[Scalar]` type does not declare the configured scalar property. | Declare it, or set `ScalarAttribute.PropertyName` to the one you have. |
| `VO1006` | Warning | A `[Scalar]` type is not a `readonly record struct`. | Prefer `readonly record struct`, so equality, immutability and allocation behaviour match the contract. |
| `VO1007` | Warning | `ValueObjectDeserializationMode.Strict` is set but no `Create` overload exists to re-validate through. | Add the `Create` overload, or use the default `Hydrate` mode. |
| `VO1008` | Error | `[Scalar]` and `[ValueObject]` are both applied to one type. | Pick one: a scalar wraps a single primitive, a value object composes members. |
| `VO1016` | Warning | A value object member is mutable. | Make it `readonly`/`init`-only — a mutable member breaks immutability and makes equality unstable. |
| `VO1022` | Error | A `[Scalar<T>]` type declares a member with the name of the property the generator owns. | Remove the declaration, or use `[Scalar]` without a type argument and own the property yourself. |

## String normalization rules

| Rule | Severity | Reported when | Fix |
| --- | --- | --- | --- |
| `VO1023` | Warning | Built-in string normalization is configured but the type implements `OnNormalize` or declares its own `Create`. | Remove the option, or move the normalization into the hook/`Create`. The hand-written path wins either way. |
| `VO1024` | Warning | Built-in string normalization is configured on a non-string scalar or member. | Remove the option; `Trim`/`Casing` apply only to `string`. |
| `VO1025` | Error | `[StringNormalize]` is on a member of a `[Scalar]`, on a type that is neither `[Scalar]` nor `[ValueObject]`, or on a property that is not a value member. | Move it to a string member of a complex `[ValueObject]`; on a scalar set `Trim`/`Casing` on `[Scalar]`. |
| `VO1026` | Error | An author-declared `[assembly: ValueObjectDefaults]` repeats a value already supplied by the packaged targets from `ValueObjectsDefault*` MSBuild properties. | Remove the argument, or set a different value. |
| `VO1027` | Error | A `[Scalar]`/`[ValueObject]` option repeats the effective assembly default. | Remove the option; it repeats the inherited value. |

Restating an assembly or type default is reported as `VO1026`/`VO1027` so each option has a single source of
truth. Only `[StringNormalize]` remains a per-member override outside that check; a value that changes the
effective configuration still raises `VO1023`/`VO1024` where those rules apply.

## ZodSharp validation rules

| Rule | Severity | Reported when | Fix |
| --- | --- | --- | --- |
| `VO1013` | Warning | `OnValidate` is declared but never invoked, because `ZodSchemaMode.InsteadOfHooks` is set. | Remove `OnValidate`, or choose a mode that keeps the hooks (`InAdditionToHooks`). |
| `VO1015` | Error | The ZodSharp `SchemaName` is not a valid C# identifier. | Give it a valid identifier. |

## Entity Framework rules

| Rule | Severity | Reported when | Fix |
| --- | --- | --- | --- |
| `VO1009` | Warning | EF mapping is requested but `Microsoft.EntityFrameworkCore` is not referenced. | Add the package reference, or stop requesting EF mapping. |
| `VO1010` | Warning | EF auto-conversion was skipped: the underlying type is not mappable. | Map it by hand with a custom `ValueConverter`. |
| `VO1017` | Warning | EF JSON mapping is requested without the JSON converter. | Register the JSON converter for the value object. |
| `VO1018` | Warning | EF complex mapping cannot convert one of the members. | Simplify the member, or map it explicitly. |
| `VO1019` | Warning | EF complex-type mapping is requested but the project resolves to EF Core 7 or earlier. | Target EF Core 8+, or choose a different `EntityFrameworkMapping`. |
| `VO1021` | Warning | EF key value generation is unavailable for the value object. | Assign keys explicitly rather than relying on store generation. |

## Retired identifiers — never reused

`VO1005`, `VO1011`, `VO1012`, `VO1014` and `VO1020` were used during development and withdrawn before
release. No version reports them, and the numbering is intentionally discontinuous. An occurrence of
one of these IDs means stale tooling or a stale suppression: remove it.

`VO1005` is worth calling out. It was catalogued as an error ("scalar constructor is missing") and
documented as requiring you to declare a constructor. It never had a reporting site, and the
requirement was the **opposite** of how the generator works: when a `[Scalar]` type declares no
constructor taking the underlying type, the generator emits a private one. Nothing ever asked you to
write it.

## Adding or changing a rule

Rules are tracked in `src/src/SourceGenerator/AnalyzerReleases.Shipped.md` and
`AnalyzerReleases.Unshipped.md`, which Roslyn's `RS2008` catalogue rule enforces. A new or changed
diagnostic must be recorded in the same change; unshipped entries move into a new
`## Release <version>` block when that version ships. Never reuse a retired identifier.
