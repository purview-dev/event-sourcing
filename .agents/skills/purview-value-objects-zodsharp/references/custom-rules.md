# Custom ZodSharp rules for value objects

## Writing a rule attribute

A custom rule reaches a value object as a `[ZodRule]`-mapped attribute, in one of two ways.

### 1. Map a hand-authored attribute to the rule

Declare a `ValidationAttribute` and point it at the rule. The open generic form serves both a
primitive member and a scalar:

```csharp
using ZodSharp.Core;

// Reads the value object through IScalarValueObject<TSelf, TValue>. Implementing IZodRule lets the
// rule own its error identity, so one attribute can report a different code per scalar.
public readonly record struct NoWhitespaceRule<TSelf>(string? Message = null)
    : IValidationRule<TSelf>, IZodRule
    where TSelf : IScalarValueObject<TSelf, string>
{
    public const string ErrorCode = "invalid_string";
    public const string MessageFormat = "Value must not contain whitespace.";

    public bool IsValid(in TSelf value) => !value.Value.Any(char.IsWhiteSpace);

    public string GetErrorMessage(in TSelf value) => Message ?? MessageFormat;

    string? IZodRule.Code => ErrorCode;

    string? IZodRule.Origin => "value_object";
}

[ZodRule(typeof(NoWhitespaceRule<>))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property)]
public sealed class NoWhitespaceAttribute : ValidationAttribute
{
    public string? Message { get; set; }
}
```

### 2. Let the generator write the attribute

Mark the rule with the parameterless `[ZodRule]` marker and ZodSharp emits a `{RuleName}Attribute` in
the rule's namespace. A trailing `Rule` becomes `Attribute`; a name clashing with a
`System.ComponentModel.DataAnnotations` attribute gets a `Zod` suffix:

```csharp
[ZodRule] // the generator emits NoWhitespaceAttribute
public readonly record struct NoWhitespaceRule<TSelf>(string? Message = null)
    : IValidationRule<TSelf>, IZodRule
    where TSelf : IScalarValueObject<TSelf, string>
{
    // ...
}
```

The generator runs over the compilation that **declares** the rule, so the marker form suits a rule
in the same project, or in a rules library that references Purview.ZodSharp.

Requirements the generator enforces:

- A rule accepting a `code`/`origin` constructor parameter must implement `IZodRule`, or
  `ZODSGEN039`.
- A rule should expose `public const string ErrorCode` and `public const string MessageFormat`, or
  `ZODSGEN042`.
- `ZODSGEN043` reports a built-in rule that cannot produce a validation attribute.

## How `Code` and `Origin` resolve

First match wins:

1. **Rule-owned** — the rule implements `IZodRule` (`IZodRule.Code` / `IZodRule.Origin`).
2. **Attribute-declared** — `Code`/`Origin` named arguments on the applied attribute.
3. **Mapping** — `[ZodRule(typeof(X), Code = "…", Origin = "…")]` on the attribute type.
4. **Default** — `validation_failed`, no origin.

The value-object layer never reconstructs codes or messages. It calls
`{Type}Schema.Validate(instance)` and forwards `result.Errors` into a `ZodException`, so `Code`,
`Message`, `Origin`, `Category` and path arrive intact.

## Reusing a rule written against the underlying value

A rule written against the underlying value — `NonSentinelRule<Guid>` — validates *that value*, not
the value object. Closing it with the scalar type (`NonSentinelRule<AssetId>`) compiles but **always
passes**, because the sentinel check only knows the primitive.

The value-object generator emits `ScalarRuleAdapter<TSelf, TValue, TRule>` into every project that
references both `Purview.ValueObjects` and `Purview.ZodSharp`:

```csharp
internal readonly record struct ScalarRuleAdapter<TSelf, TValue, TRule>(TRule Rule) : IValidationRule<TSelf>
    where TSelf : IScalarValueObject<TSelf, TValue>
    where TRule : IValidationRule<TValue>
{
    public bool IsValid(in TSelf value) => Rule.IsValid(value.Value);

    public string GetErrorMessage(in TSelf value) => Rule.GetErrorMessage(value.Value);
}
```

**Do not declare it yourself.** A project that already declares its own copy — the guidance before
the generator emitted it — is detected and the generated copy is skipped, so both keep compiling.

ZodSharp applies the adapter automatically when a rule is applied to a `[Scalar]` type, so the common
case needs no extra code. The built-in `[NonSentinel]` is exactly this shape:

```csharp
using ZodSharp;
using ZodSharp.Rules;

[Scalar]
[ZodSchema]
[NonSentinel(Message = "UserId must not be empty.")]
public readonly partial record struct UserId
{
    public Guid Value { get; init; }
}
```

The generator closes `NonSentinelRule<>` with the underlying `Guid` and wraps it in
`ScalarRuleAdapter<UserId, Guid, NonSentinelRule<Guid>>`, so the rule runs against the value object
with an empty path, and the **wrapped** rule owns the reported `Code` (the built-in rules report no
structured `Origin`). One attribute serves every scalar backed by the same primitive.

The runnable version is `src/src/ZodSharpSample/NonSentinelModels.cs`, and the fixtures in
`src/tests/ValueObjects.IntegrationTests/Serialization/ZodSchemaRuleModels.cs` cover rule
`Code`/`Origin` and the empty path end to end.

See ZodSharp's [Custom Rules](https://purview.dev/docs/zodsharp/custom-rules/) for the rule contract
and the full list of shipped attributes.
