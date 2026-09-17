# Purview.EventSourcing.Validation.ZodSharp

`Purview.EventSourcing.Validation.ZodSharp` adapts [Purview.ZodSharp](https://github.com/purview-dev/zodsharp) schema validators to the Purview EventSourcing aggregate validation contract (`IAggregateValidator<TAggregate>`).

## Install

```bash
dotnet add package Purview.EventSourcing.Validation.ZodSharp
```

## Register the adapter

### With an explicit schema validator implementation

Registers both the Purview.ZodSharp schema validator and the aggregate-validator adapter:

```csharp
builder.Services.AddZodSharpAdapter<OrderAggregate, OrderSchema>();
```

### When schema validators are already registered

Use this overload when the schema validator is already registered in the container. The adapter resolves `IZodSchemaValidator<TAggregate>` from the container:

```csharp
builder.Services.AddZodSharpAdapter<OrderAggregate>();
```

Both overloads accept an optional `ServiceLifetime` (defaults to `Singleton`).

## Important: reference Purview.ZodSharp directly

The adapter's public surface exposes `ZodSharp.Core.IZodSchemaValidator<TAggregate>`, so the consuming project **must** include a direct `PackageReference` to `Purview.ZodSharp`. The package enforces this with a build-time guardrail: the build fails with a clear error if you reference this package without also referencing `Purview.ZodSharp`.

```xml
<ItemGroup>
  <PackageReference Include="Purview.EventSourcing.Validation.ZodSharp" Version="..." />
  <PackageReference Include="Purview.ZodSharp" Version="..." />
</ItemGroup>
```

See `docs/wiki/Dependency-Guardrails.md` for the full rationale.

## Typical usage

```csharp
// The store invokes the registered IAggregateValidator<TAggregate> before saving.
await store.SaveAsync(order, cancellationToken);
```

## What it provides

- `ZodSharpAggregateValidator<TAggregate>` - converts Purview.ZodSharp validation results into the framework's `Purview.EventSourcing.Validation.ValidationResult`
- `IAggregateValidator<TAggregate>` registration consumed by the event store during save
- A `buildTransitive` target that validates the direct `Purview.ZodSharp` reference

## Notes

- This package is optional. The core package never acquires a mandatory Purview.ZodSharp dependency.
- Validation is invoked by the event store when an aggregate is saved; failing validations produce a non-saved save result.

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)

## Related packages

- [Core package](https://github.com/purview-dev/event-sourcing/blob/main/src/src/EventSourcing/Sdk/README.md): `Purview.EventSourcing`
- [FluentValidation integration](https://github.com/purview-dev/event-sourcing/blob/main/src/src/FluentValidationImpl/Sdk/README.md): `Purview.EventSourcing.FluentValidation`