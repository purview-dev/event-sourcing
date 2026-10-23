---
name: purview-value-objects-zodsharp
description: >-
  Use when validating Purview.ValueObjects with Purview.ZodSharp - the [ZodSchema] integration that
  routes Create through a generated schema, ZodSchemaMode, the OnZodValidate refinement hook, custom
  rules and [ZodRule] attributes on scalars, schema-first Z.* validation, DTO validation, and mapping
  ZodException to ASP.NET Core Problem Details.
metadata:
  product: Purview.ValueObjects
  repository: purview-dev/value-objects
---

# Validating value objects with ZodSharp

[Purview.ZodSharp](https://www.nuget.org/packages/Purview.ZodSharp) is a C# port of
[Zod](https://github.com/colinhacks/zod). It complements `Purview.ValueObjects` by splitting the
responsibility: **the value object owns the invariant, ZodSharp owns the rule definition and the
result shape**.

Reference the package explicitly when you use its types — do not rely on transitive flow. The
ZodSharp source generator is active in any project that references the package.

```text
dotnet add package Purview.ZodSharp
```

Four ways to use it, in rough order of how often you want them:

1. **Generator-integrated** — `[Scalar]` + `[ZodSchema]`, and `Create` validates through the schema.
2. **Generated validators** — `[ZodSchema]` + DataAnnotations emits a zero-allocation `{Type}Schema`.
3. **Schema-first** — hand-build `Z.String()` / `Z.Number()` / `Z.Enum<T>()` and construct through `Create`.
4. **DTO-first** — validate the request shape, then map onto value objects.

## 1. Generator-integrated validation

Annotate a value object with **both** `[Scalar]`/`[ValueObject]` **and** `[ZodSchema]`, and the
value-object generator routes the generated `Create` through the ZodSharp schema — no wiring:

```csharp
using System.ComponentModel.DataAnnotations;
using Purview.ValueObjects.Serialization;
using ZodSharp;

[Scalar]
[ZodSchema]
public readonly partial record struct EmailAddress
{
    [EmailAddress]
    [StringLength(254, MinimumLength = 3)]
    public string Value { get; }

    static partial void OnNormalize(ref string value) => value = value?.Trim().ToLowerInvariant()!;
}

EmailAddress.Create("not-an-email");   // throws ZodException via EmailAddressSchema.Validate
```

`Create` constructs the instance, calls `{Type}Schema.Validate(instance)` and throws `ZodException`
on failure. **`Hydrate` stays replay-safe** — no schema, no hooks.
`ValueObjectDeserializationMode.Strict` deserializes through `Create`, so it picks the schema up too.

`[ZodSchema]` reads DataAnnotations: `[Required]`, `[StringLength]`, `[Range]`,
`[RegularExpression]`, `[EmailAddress]`, `[AllowedValues]`, `[DeniedValues]`. It supports classes,
structs and records.

### ZodSchemaMode

| Mode | Behaviour |
| --- | --- |
| `InAdditionToHooks` (default) | the schema runs **and** `OnValidate` runs |
| `InsteadOfHooks` | the schema runs **instead of** `OnValidate`; `OnNormalize` still runs first |

Leaving an `OnValidate` in place under `InsteadOfHooks` makes it unreachable — that is `VO1013`.

### The automatic scalar form

`[Scalar<T>]` / `[Scalar(typeof(T))]` have the value-object generator declare the underlying
property, so a property-attached DataAnnotation has no host. For those types use Zod-native
**type-level** rules (`[RequiredZod]`, `[ZodRule(typeof(...))]`), which ZodSharp reads from the type
and adapts to the scalar's underlying value — or keep the manual `[Scalar]` form when you need
property-level DataAnnotations. Purview.ZodSharp 2.0.2+ generates `{Type}Schema` from the attribute
itself, so the automatic form validates through `Create` exactly like the manual one.

A nullable scalar (`Nullable = true`, or a `T?` value type) round-trips JSON `null`, so its schema
and any string rule must be null-tolerant: use the shipped `[NullOrNonWhiteSpace]`, which accepts
`null` but rejects whitespace, rather than `[RequiredZod]`, which always rejects `null`.

### `[ZodSchema]` options

- `SchemaName` — overrides the generated class name (default `{TypeName}Schema`); the DI adapter
  becomes `{SchemaName}Validator`. ZodSharp applies any non-empty value **verbatim**, including
  whitespace, so an unusable name is reported as `VO1015` rather than silently falling back.
- `CustomValidationMethodName` — a static async method the generated validator's `ValidateAsync`
  awaits after the synchronous rules pass (default `CustomValidationAsync`).

## The `OnZodValidate` refinement hook

For rules DataAnnotations cannot express — cross-member invariants, allowed domains, state checks —
implement the partial hook the ZodSharp generator declares:

```csharp
[Scalar]
[ZodSchema]
public readonly partial record struct CorporateEmail
{
    [EmailAddress]
    public string Value { get; }

    static partial void OnNormalize(ref string value) => value = value?.Trim().ToLowerInvariant()!;

    partial void OnZodValidate(RefineCtx<CorporateEmail> context)
    {
        if (!context.Value.Value.EndsWith("@contoso.com", StringComparison.Ordinal))
            context.AddIssue("invalid_domain", "Corporate emails must use the contoso.com domain.", [nameof(Value)]);
    }
}
```

Facts worth knowing:

- The hook is declared **and invoked by the ZodSharp generator**, inside `{Type}Schema.Validate`, so
  it runs for every entry point: `Validate`, `Parse`, the DI adapter, `IValidateOptions`, the
  generated `Create`, and `Strict` deserialization. `Hydrate` never validates.
- Because it belongs to the schema, `{Type}Schema.Validate(instance)` reports exactly what `Create`
  reports.
- The type and every containing type must be `partial`, so the generator can declare the hook.
  ZodSharp reports `ZODSGEN034` (not partial) and `ZODSGEN035` (malformed signature).
- It is independent of `ZodSchemaMode`: `InsteadOfHooks` skips `OnValidate`, never `OnZodValidate`.
- The old `IEnumerable<ValidationError> Validate()` refinement contract is **retired**; ZodSharp
  reports `ZODSGEN036` for a member still using it.

## Custom rules on scalars

A scalar is validated as a **unit** — its single `Value` *is* the value. ZodSharp ships a validation
attribute for every built-in rule in `ZodSharp.Rules`:

| Attribute | Rule | Notes |
| --- | --- | --- |
| `[NonSentinel(Message = "…")]` | `NonSentinelRule<T>` | Rejects `Guid.Empty`, the `DateTime`/`DateTimeOffset`/`DateOnly`/`TimeOnly` bounds, and null/empty/whitespace strings. |
| `[Email]` | `EmailRule` | |
| `[E164]` | `E164Rule` | |
| `[UUID(UuidVersion.V4)]` | `UUIDRule` | A rule value with no default is a required constructor argument. |
| `[MinLengthZod(3)]` | `MinLengthRule` | The `Zod` suffix avoids the DataAnnotations clash. Also `[MaxLengthZod]`, `[UrlZod]`, `[PhoneZod]`, `[CreditCardZod]`, `[Base64StringZod]`. |

Each attribute mirrors its rule's constructor; `Message` and `Code` stay named properties, so an
error code or message can be overridden per use.

```csharp
using ZodSharp;
using ZodSharp.Rules;

[Scalar]
[ZodSchema]
[NonSentinel(Message = "AssetId must not be empty.")]
public readonly partial record struct AssetId
{
    public Guid Value { get; init; }
}
```

`AssetId.Create(Guid.Empty)` throws a `ZodException` whose error carries the rule's own `Code`, no
structured `Origin`, and an **empty path** — the rule applies to the value object, not a member.
Because it runs inside the schema, `Hydrate` stays replay-safe.

Writing your own rule attribute, the automatic adapter that lets a rule written against the
underlying value validate the value object, and the `Code`/`Origin` resolution order are in
[references/custom-rules.md](references/custom-rules.md).

## Schema-first validation

When you do not want the generator involved, build a schema for the **underlying** value and
construct through `Create`:

```csharp
using ZodSharp;

public static class ScalarSchemas
{
    public static readonly IZodSchema<string, string> EmailSchema = Z.String().Email().Min(3).Max(254);
    public static readonly IZodSchema<string, string> CurrencySchema = Z.String().Regex("^[A-Z]{3}$");
    public static readonly IZodSchema<OrderStatusKind, OrderStatusKind> OrderStatusSchema = Z.Enum<OrderStatusKind>();
    public static readonly IZodSchema<double, double> MoneyAmountSchema = Z.Number().Positive();
}
```

ZodSharp validates the raw value **exactly as supplied** — normalization is the value object's job in
`OnNormalize`. So: validate the raw input, then `Create`, which normalizes and wraps it.

## Validating DTOs before mapping

```csharp
[ZodSchema]
public sealed partial class RegistrationDto
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Range(13, 120)]
    public int Age { get; init; }

    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    partial void OnZodValidate(RefineCtx<RegistrationDto> context)
    {
        if (context.Value.Name.StartsWith("x", StringComparison.OrdinalIgnoreCase))
            context.AddIssue("name", "Name cannot start with 'x'.", [nameof(Name)]);
    }
}

var result = RegistrationDtoSchema.Validate(dto);

if (result.IsSuccess)
{
    var email = EmailAddress.Create(result.Value.Email);
    var money = Money.Create(19.99m, CurrencyCode.Create("USD"));
}
```

Async rules go through `CustomValidationMethodName`, naming a
`static ValueTask<ValidationResult<T>> Method(T value, CancellationToken)` that the generated
`{Type}SchemaValidator.ValidateAsync` awaits once the synchronous rules pass.

## Dependency injection

`ZodSchemaFactory` resolves validators by validated type:

```csharp
using ZodSharp.Core;

ZodSchemaFactory factory = new();

factory.Register(new EmailAddressSchemaValidator());                         // generated adapter
factory.Register(new ZodSchemaValidator<string>(ScalarSchemas.EmailSchema)); // hand-built

var emailResult = factory.Validate(EmailAddress.Create("demo@example.com"));
```

## Error handling

`ValidationResult<T>` is a struct with `IsSuccess`, `Value` (only on success) and `Errors`
(`ImmutableArray<ValidationError>`). Each `ValidationError` carries `Code`, `Message` and `Path`,
plus optional `Origin`, `Category` and size bounds:

```csharp
foreach (var error in result.Errors)
    Console.WriteLine($"{string.Join(".", error.Path)}: {error.Message} ({error.Code}, {error.Origin})");
```

Use `Parse` / `GetValueOrThrow()` to throw instead of inspecting. The value-object layer never
reconstructs codes or messages — it forwards `result.Errors` into a `ZodException`, so a rule's
`Code`, `Message`, `Origin`, `Category` and path arrive intact.

### ASP.NET Core Problem Details

`Purview.ZodSharp.AspNetCore` maps a thrown `ZodException` to `HttpValidationProblemDetails`:

```csharp
builder.Services.AddZodSharpProblemDetails();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
```

Make the value object validate during model binding:

```csharp
[Scalar(
    ZodSchemaMode = ZodSchemaMode.InsteadOfHooks,
    DeserializationMode = ValueObjectDeserializationMode.Strict)]
[ZodSchema]
public readonly partial record struct EmailAddress
{
    [Required, EmailAddress]
    public string Value { get; }
}

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new ScalarJsonConverterFactory()));
```

An invalid body now returns `400 application/problem+json` with the structured issues in the `issues`
extension.

Map error codes to HTTP statuses with `[ErrorType]` on a `static readonly ErrorType` field; the
bundled `ErrorTypeGenerator` emits `Create{Field}(...)` and a `[DoesNotReturn]` `Throw{Field}(...)`.
The `ZODSASP001` analyzer flags a `MessageFormat` placeholder missing from `Parameters` at compile
time.

## JSON Schema export

```csharp
var jsonSchema = Z.ToJsonSchema(ScalarSchemas.EmailSchema, new ToJsonSchemaOptions { Title = "Email" });
```

Draft 2020-12, for sharing a contract with TypeScript Zod.

## Diagnostics

`VO1013` (unreachable `OnValidate` under `InsteadOfHooks`) and `VO1015` (invalid `SchemaName`) are the
value-object side. ZodSharp's own `ZODSGEN030`-`ZODSGEN043` cover the refinement hook and rule
mapping; `ZODSASP001` covers the ASP.NET Core error types.

## Testing the dual-generator integration

Two shapes, and the distinction is load-bearing:

- **In-memory (unit)** — `src/tests/SourceGenerator.UnitTests` registers the packaged ZodSharp
  generator through `ZodSchemaValidationGeneratorTestOptions`, resolving the component types out of
  band via `Common/ZodSharpSourceGenerators.cs`. The project copies
  `analyzers/dotnet/cs/Purview.ZodSharp.SourceGenerators.dll` beside the test binaries
  (`GeneratePathProperty` + `None`/`CopyToOutputDirectory`) and loads it with `Assembly.LoadFrom`.
  **Never turn that copy into a `<Reference>`**: a merged analyzer component carried
  `Purview.SourceGeneratorFramework.*` types that then collided (`CS0433`) with the framework
  assembly the harness loads. `Common/ZodSharpSourceGeneratorsTests.cs` guards it.
- **Real compile (integration)** — `src/tests/ValueObjects.IntegrationTests` declares `[Scalar]` +
  `[ZodSchema]` fixtures and asserts runtime behaviour; both generators run in the real compiler, so
  nothing is reflected or registered.

The loaded generator carries its own framework implementation, so it keeps its own log sink and
CodeWriter scope validation: do not assert on its log entries, and leave `ValidateCodeWriterScopes`
off for that run.

`src/src/ZodSharpSample` is the runnable example.
