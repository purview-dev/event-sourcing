---
name: purview-zodsharp-aspnetcore
description: >-
  Use when wiring Purview.ZodSharp into ASP.NET Core or any DI host - ProblemDetails from a
  ValidationResult or a thrown ZodException, minimal-API endpoint validation, registering
  IZodSchemaFactory with AddZodSharp or AddZodSharpFactory, validating options objects with
  IValidateOptions, and mapping error codes to HTTP statuses with ErrorType.
license: MIT
metadata:
  product: Purview.ZodSharp.AspNetCore
  repository: purview-dev/zodsharp
---

# ASP.NET Core and dependency injection

`Purview.ZodSharp.AspNetCore` turns failed validation into standard `ProblemDetails` /
`HttpValidationProblemDetails` payloads **without losing the structured issues**, and registers
schema resolution into DI.

```bash
dotnet add package Purview.ZodSharp.AspNetCore
```

## ProblemDetails from a result

```csharp
using ZodSharp.AspNetCore;

var result = BasketSchema.Validate(basket);

if (!result.IsSuccess)
{
    var problem = result.ToHttpValidationProblemDetails();

    return Results.ValidationProblem(
        problem.Errors,
        extensions: new Dictionary<string, object?> { ["issues"] = problem.Extensions["issues"] });
}
```

| Member | Produces |
| --- | --- |
| `ToHttpValidationProblemDetails<T>(result, statusCode = 400)` | `HttpValidationProblemDetails`; paths flattened to dotted keys (`user.email`, array indexes as `[0]`) |
| `ToValidationProblemDetails<T>(result, statusCode = 400)` | `ValidationProblemDetails` |

Both **throw `InvalidOperationException` when the result is successful** — they are failure
converters, not general mappers. The structured metadata survives in the `issues` extension as a
`ValidationIssue[]`:

```csharp
public sealed class ValidationIssue
{
    public required string Code { get; init; }
    public string? Category { get; init; }
    public string? Origin { get; init; }
    public int? Minimum { get; init; }
    public int? Maximum { get; init; }
    public bool? Inclusive { get; init; }
    public required string[] Path { get; init; }
    public required string Message { get; init; }
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }
}
```

## Minimal APIs

```csharp
// Validate the bound DTO automatically - needs AddZodSharp to have registered the validator.
app.MapPost("/users", (UserDto dto) => dto)
   .WithZodSharpValidation<UserDto>();

// Or validate in the handler and return the IResult directly.
app.MapPost("/users", (UserDto dto) =>
{
    var result = UserDtoSchema.Validate(dto);
    return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToValidationProblem();
});
```

`WithZodSharpValidation<T>()` adds an endpoint filter that validates the first bound argument of type
`T` against the `IZodSchemaFactory` registered by `AddZodSharp`/`AddZodSharpFactory`. On failure it
returns the same payload as `ToHttpValidationProblemDetails()`; on success it passes through.

## Thrown `ZodException`

A `ZodException` from `Parse`, `GetValueOrThrow()`, or a value object's generated `Create` under
strict deserialization maps the same way:

```csharp
catch (ZodException ex)
{
    return Results.ValidationProblem(ex.ToHttpValidationProblemDetails().Errors);
}
```

Or handle it globally:

```csharp
builder.Services.AddZodSharpProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
```

`AddZodSharpProblemDetails(configure?, problemDetails?)` registers `ZodExceptionHandler`, calls
`services.AddProblemDetails(problemDetails)`, and configures `ZodProblemDetailsOptions`:

- `ErrorTypeRegistry Registry` — resolves codes to `ErrorType`s; defaults to `ErrorTypeRegistry.Default`.
- `bool FormatMessages` — format messages from the matched `ErrorType.MessageFormat`.
- `Func<ImmutableArray<ValidationError>, int>? StatusCodeSelector` — total control of the status code.

> It does **not** register `IZodSchemaFactory`. Pair it with `AddZodSharp` (or the core
> `AddZodSharpFactory`) when you also want DI validator resolution.

## Error codes to HTTP statuses

`ErrorType`, the `[ErrorType]` attribute, the generator that emits `Create`/`Throw` helpers and the
`ZODSASP001`–`ZODSASP003` analyzers all ship in the **core** package and need no ASP.NET Core
dependency. The AspNetCore package adds `ErrorTypeRegistry` and the ProblemDetails mapping on top.

```csharp
public static partial class ConcurrentErrorType
{
    [ErrorType]
    public static readonly ErrorType SaveFailed = new(
        Code: "aggregate_save_failed",
        Category: "invalid_value",
        Description: "The aggregate could not be saved.",
        HttpStatus: 409,
        MessageFormat: "Aggregate '{AggregateId}' (of type {AggregateType}) failed to save",
        Parameters:
        [
            new("AggregateId", typeof(string)),
            ErrorType.Param<string>("AggregateType"),
        ]);
}

ErrorTypeRegistry.Default.Register(ConcurrentErrorType.SaveFailed);
```

`HttpStatus` lives on `ErrorType` purely for convenience — the core library never reads it; only the
ASP.NET Core mapping does. `Category` is a broad grouping that can span many specific codes (code
`invalid_tenant_id`, category `invalid_value`); the generated helpers copy it onto each
`ValidationError` and it surfaces on the serialized `ValidationIssue`.

Marking the field `[ErrorType]` and the containing class `partial` gets you strongly typed helpers,
one parameter per declared entry:

```csharp
var error = ConcurrentErrorType.CreateSaveFailed("agg-123", "Invoice");
ConcurrentErrorType.ThrowSaveFailed("agg-123", "Invoice");

var detailed = ConcurrentErrorType.CreateSaveFailed(
    "agg-123", "Invoice",
    path: ["order", "items", "[0]"],
    origin: "collection", minimum: 1, maximum: 10, inclusive: true);
```

`Create` builds a typed `ErrorTypeParameters` (validated against the declared types, exposed through
`ValidationError.Parameters`) and sets the message from `ErrorType.FormatMessage`, so
`error.Message` already reads the formatted text.

Mapping rules:

- **Status** — the highest matched `ErrorType.HttpStatus` wins; unmapped codes fall back to `400`.
  `StatusCodeSelector` overrides everything.
- **Title / Type / Detail** — from the highest-status matched `ErrorType`, otherwise defaulted.
- **Message** — named placeholders are substituted from `ValidationError.Parameters`. A placeholder
  with no matching value is **left as-is**, so a templating gap stays visible rather than silently
  disappearing. `ZODSASP001` warns at compile time when a placeholder is not declared in `Parameters`.
- **Parameters** — surfaced per-issue in `issues`, and merged (camel-cased) into the top-level
  extensions for client correlation.

`ToHttpValidationProblemDetails` / `ToValidationProblemDetails` also accept an `ErrorTypeRegistry` or
a `Func<string, ErrorType?>` for on-demand mapping.

## Registering the factory

`IZodSchemaFactory` (`ZodSharp.Core`) resolves validators by type:

| Member | Behaviour |
| --- | --- |
| `Resolve<T>()` | the validator, or `null` |
| `ResolveRequired<T>()` | the validator, or `InvalidOperationException` |
| `Validate<T>(value)` | validate through the registered validator |
| `Register<T>(…)` / `Register(Type, …)` / `TryRegister<T>(…)` / `IsRegistered<T>()` | registration |

```csharp
builder.Services.AddZodSharp(options =>
{
    options.ScanAssemblies.Add(typeof(UserDto).Assembly);
    options.ScanAssemblyGraphs.Add(typeof(Program).Assembly);
    options.ScanLoadedAssemblies = true;
});

// Additive, for modular apps:
builder.Services.AddZodSharpAssembly(typeof(UserDto).Assembly);
builder.Services.AddZodSharpAssemblyGraph(typeof(Program).Assembly);
builder.Services.AddZodSharpLoadedAssemblies();
```

Scanning finds the generator's `[assembly: ZodSchemaGenerated(typeof(…))]` markers and registers each
`{Type}SchemaValidator`. A hand-built schema registers by wrapping:
`factory.Register(new ZodSchemaValidator<Dictionary<string, object?>>(mySchema))`.

> **`TryAdd` semantics.** The **first** `AddZodSharp`/`AddZodSharpFactory` call wins; later calls —
> including their options and `ConfigureFactory` callbacks — are ignored. State is never overwritten,
> so calling it more than once is safe, and the additive helpers still contribute.

| Method | Package | Registers | Use when |
| --- | --- | --- | --- |
| `AddZodSharp(options)` | AspNetCore | factory + auto-registered generated validators | an ASP.NET Core app |
| `AddZodSharpAssembly/AssemblyGraph/LoadedAssemblies` | AspNetCore | additive assembly sources | modular apps where deeper layers contribute their own schemas |
| `AddZodSharpFactory(configure)` | core | the factory only | any host; you call `RegisterFromAssembly` yourself |
| `AddZodSharpProblemDetails(…)` | AspNetCore | exception handler + ProblemDetails, **not** the factory | mapping thrown `ZodException`s |
| `AddZodSchemaOptionsValidator<T>(…)` | core | `IValidateOptions<T>` | options validation; needs a factory first |

## Options validation

```csharp
builder.Services.AddZodSharpFactory(f => f.RegisterFromAssembly(typeof(UserOptions).Assembly));
builder.Services.AddZodSchemaOptionsValidator<UserOptions>();

// or chained:
builder.Services
    .AddOptions<UserOptions>()
    .Bind(configuration.GetSection("User"))
    .AddZodSchemaValidator()
    .ValidateOnStart();           // fail at startup, not first access
```

`MissingValidatorBehavior` decides what happens with no validator registered for `T`:
`Throw` (default — `ResolveRequired<T>()`, so a missing schema is loud rather than a silent skip) or
`Ignore` (passes through as success).

The generator also auto-generates `IValidateOptions<T>` for types whose names end in configurable
suffixes — see the `purview-zodsharp-source-generator` skill.

## Related skills

`purview-zodsharp-core` for the runtime API, `purview-zodsharp-source-generator` for the compile-time
validators this resolves, and `purview-zodsharp-diagnostics` for `ZODSASP*`.
