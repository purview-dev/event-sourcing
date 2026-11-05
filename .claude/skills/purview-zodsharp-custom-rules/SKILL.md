---
name: purview-zodsharp-custom-rules
description: >-
  Use when writing a custom validation rule for Purview.ZodSharp - implementing IValidationRule and
  IStringValidationRule, the ErrorCode/MessageFormat constants convention, IZodRule runtime identity,
  attaching a rule with AddRule or a fluent extension, exposing it as a DataAnnotations-style
  attribute with [ZodRule], generic rules, and type-level rules on scalar value objects.
license: MIT
metadata:
  product: Purview.ZodSharp
  repository: purview-dev/zodsharp
---

# Custom rules

A **rule** is a `readonly record struct` (or class) implementing `ZodSharp.Core.IValidationRule<T>`.
Rules run after a schema's type and structural check succeeds; each failing rule adds a
`ValidationError`.

Rules are first class in two directions:

- attach one to a schema with the public `AddRule`/`Rule` API, or
- surface it as a `System.ComponentModel.DataAnnotations`-style attribute that the `[ZodSchema]`
  generator honours exactly like `[EmailAddress]` or `[Range]`.

## The contract

```csharp
namespace ZodSharp.Core;

public interface IValidationRule<T>
{
    bool IsValid(in T value);
    string GetErrorMessage(in T value);
}
```

Implement it as a **struct** so validation does not allocate. `IsValid` is called only when the
surrounding schema succeeded and — for a nullable property — the value is not `null`.

For a **string** rule, also implement `ZodSharp.Core.IStringValidationRule`
(`bool IsValid(ReadOnlySpan<char>)` / `string GetErrorMessage(ReadOnlySpan<char>)`) so the rule
participates in `ZodString.ValidateSpan`/`IsValidSpan` without materialising the input. A rule that
implements only `IValidationRule<T>` still works; it falls back to the string pipeline.

## `ErrorCode` and `MessageFormat` — not optional in practice

Every rule **should** expose its error identity as public constants, so code and tests assert against
the rule rather than re-typing literals:

```csharp
public readonly record struct EmailRule : IValidationRule<string>
{
    public const string ErrorCode = "invalid_string";
    public const string MessageFormat = "Invalid email format: {0}";

    public string Code => ErrorCode;

    public string GetErrorMessage(in string value) =>
        string.Format(CultureInfo.CurrentCulture, MessageFormat, value);
}
```

- **`ErrorCode`** is the canonical code. `IValidationRule<T>.Code` defaults to `"validation_failed"`;
  a rule accepting a per-usage `code` override returns it (falling back to `ErrorCode`) from both
  `Code` and `IZodRule.Code`, so every route reports the same effective value.
- **`MessageFormat`** is a `string.Format` template. `{0}` is the offending value; `{1}`, … are
  rule-specific arguments. Format with `CultureInfo.CurrentCulture`.
- The constants may be inherited from a base rule class, and **abstract bases are exempt**, so a
  shared base can host them for its concrete derivations.

A source-declared rule missing either constant is reported as **`ZODSGEN042`**. The payoff:

```csharp
await Assert.That(error.Code).IsEqualTo(EmailRule.ErrorCode);
await Assert.That(error.Message).IsEqualTo(
    string.Format(CultureInfo.CurrentCulture, EmailRule.MessageFormat, value));
```

### Runtime identity: `IZodRule`

The constants are the rule's *static* default. When one attribute must report a different code per
annotated member, implement `IZodRule` and supply code/origin at runtime:

```csharp
public interface IZodRule
{
    string? Code { get; }     // null falls back to the attribute-mapped code
    string? Origin { get; }   // null falls back to the attribute-mapped origin
}
```

The generated code casts to `IZodRule` when reading them, so identity can depend on constructor
arguments. A rule that **accepts** a `code`/`origin` constructor parameter without implementing
`IZodRule` never reaches the reported identity — that is `ZODSGEN039`.

Code and origin resolve first-match-wins:

1. the rule, when it implements `IZodRule`;
2. `Code`/`Origin` named arguments on the applied attribute;
3. the `[ZodRule(typeof(X), Code = "…", Origin = "…")]` mapping on the attribute type;
4. default — `validation_failed`, no origin.

### Message precedence

1. A `message` argument on the rule (typically a `Message` constructor parameter), when set.
2. The attribute's `ErrorMessage` / `ErrorMessageResourceName` / `ErrorMessageResourceType`, mapped
   to a constructor parameter named `message`.
3. `GetErrorMessage(value)`, formatting `MessageFormat`.

`MessageFormat` is the fallback, not the only message.

## Writing one

```csharp
using System.Globalization;
using ZodSharp.Core;

namespace MyRules;

/// <summary>Rejects strings that contain whitespace.</summary>
public readonly record struct NoWhitespaceRule(string? Message = null) : IValidationRule<string>
{
    public const string ErrorCode = "invalid_string";
    public const string MessageFormat = "Whitespace is not allowed in '{0}'.";

    public bool IsValid(in string value)
    {
        if (value is null)
            return false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
                return false;
        }

        return true;
    }

    public string Code => ErrorCode;

    public string GetErrorMessage(in string value) =>
        Message ?? string.Format(CultureInfo.CurrentCulture, MessageFormat, value);
}
```

It works standalone, and attaches to any schema:

```csharp
var rule = new NoWhitespaceRule();
if (!rule.IsValid("John Doe"))
    Console.WriteLine(rule.GetErrorMessage("John Doe"));

var schema = Z.String().Rule(new NoWhitespaceRule("No spaces allowed."));
```

`ZodType<TOutput, TInput>.AddRule(IValidationRule<TOutput>)` and the generic `Rule<TRule>(TRule)`
helper are both public. Give the rule a first-class fluent method with an ordinary extension method on
its schema type, the way the built-in rules are surfaced.

## Exposing it as an attribute

Two routes.

**Map a hand-authored attribute to the rule.** The open generic form serves both a primitive member
and a scalar value object:

```csharp
[ZodRule(typeof(NoWhitespaceRule<>))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property)]
public sealed class NoWhitespaceAttribute : ValidationAttribute
{
    public string? Message { get; set; }
}
```

**Or let the generator write it.** Mark the rule with the parameterless `[ZodRule]` and a
`{RuleName}Attribute` is emitted in the rule's namespace — a trailing `Rule` becomes `Attribute`, and
a name colliding with a DataAnnotations attribute takes a `Zod` suffix:

```csharp
[ZodRule]   // emits NoWhitespaceAttribute
public readonly record struct NoWhitespaceRule(string? Message = null) : IValidationRule<string>;
```

The generator runs over the compilation that **declares** the rule, so the marker form suits a rule
in the same project, or in a rules library referencing `Purview.ZodSharp`.

## Generic rules and rule families

A generic rule closed with the property type lets one attribute serve every member of that shape —
`[ZodRule(typeof(NonSentinelRule<>))]` on a `NonSentinelAttribute` covers `Guid`, `DateTime`,
`string` and the rest.

`ZodSharp.Rules.NonSentinelRule<T>` is the canonical example: it rejects the framework
default/boundary values an ORM stores to mean "no value" — `Guid.Empty`,
`DateTime.MinValue`/`MaxValue`, the `DateTimeOffset`/`DateOnly`/`TimeOnly` bounds, and
null/empty/whitespace strings. A type with no known sentinel always passes, so the rule never rejects
something it does not understand. Its JSON Schema mapping is exact for `DateOnly`, `TimeOnly`, deterministic
`DateTime` forms, and `DateTimeOffset`; the optional UTC opt-in only narrows the accepted offset (see the JSON
skill).

```csharp
var schema = Z.Date().AddRule(new NonSentinelRule<DateTime>());
var result = schema.Validate(DateTime.MinValue);
// result.Errors[0].Code == NonSentinelRule<DateTime>.ErrorCode  ("invalid_value")
```

## Type-level rules and scalar value objects

An attribute applied to a **type** validates the value as a unit, reporting an **empty path**. That is
what makes a rule usable on a `Purview.ValueObjects` `[Scalar]`:

```csharp
[Scalar]
[ZodSchema]
[NonSentinel(Message = "UserId must not be empty.")]
public readonly partial record struct UserId
{
    public Guid Value { get; init; }
}
```

A rule written against the *underlying* value — `NonSentinelRule<Guid>` — is adapted automatically:
the generator closes it with the underlying type and wraps it, so it runs against the value object
with an empty path, and the wrapped rule owns the reported `Code`. Closing such a rule with the
scalar type instead (`NonSentinelRule<UserId>`) compiles but **always passes**, because the check only
knows the primitive.

## Diagnostics you will meet

`ZODSGEN030` (mapped rule does not implement `IValidationRule<T>` for the property type),
`ZODSGEN031` (a constructor parameter could not be mapped from the attribute), `ZODSGEN032` (no
attribute could be generated), `ZODSGEN033` (a rule-mapped attribute on a type that gets no generated
schema, so the rule never runs), `ZODSGEN037`/`ZODSGEN038` (attribute-name collisions and incomplete
mappings), `ZODSGEN039` (`code`/`origin` without `IZodRule`), `ZODSGEN040` (an attribute argument with
no effect), `ZODSGEN042` (missing `ErrorCode`/`MessageFormat`) and `ZODSGEN043` (a built-in rule whose
constructor parameter cannot be an attribute property).

The `purview-zodsharp-diagnostics` skill carries the full catalogue.
