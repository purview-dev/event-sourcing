# Purview.EventSourcing.ZodSharp

`Purview.EventSourcing.ZodSharp` adapts [ZodSharp](https://github.com/RemiBou/ZodSharp) schema validators to the Purview Event Sourcing aggregate validation contract (`IAggregateValidator<TAggregate>`).

## Install

```bash
dotnet add package Purview.EventSourcing.ZodSharp
```

## Register the adapter

### With an explicit schema validator implementation

Registers both the ZodSharp schema validator and the aggregate-validator adapter:

```csharp
builder.Services.AddZodSharpAdapter<OrderAggregate, OrderSchema>();
```

### When schema validators are already registered

Use this overload when the schema validator is already registered in the container. The adapter resolves `IZodSchemaValidator<TAggregate>` from the container:

```csharp
builder.Services.AddZodSharpAdapter<OrderAggregate>();
```

Both overloads accept an optional `ServiceLifetime` (defaults to `Singleton`).

## Important: reference ZodSharp directly

The adapter's public surface exposes `ZodSharp.Core.IZodSchemaValidator<TAggregate>`, so the consuming project **must** include a direct `PackageReference` to `ZodSharp`. The package enforces this with a build-time guardrail: the build fails with a clear error if you reference this package without also referencing `ZodSharp`.

```xml
<ItemGroup>
  <PackageReference Include="Purview.EventSourcing.ZodSharp" Version="..." />
  <PackageReference Include="ZodSharp" Version="..." />
</ItemGroup>
```

See `docs/wiki/Dependency-Guardrails.md` for the full rationale.

## Typical usage

```csharp
// The store invokes the registered IAggregateValidator<TAggregate> before saving.
await store.SaveAsync(order, cancellationToken);
```

## What it provides

- `ZodSharpAggregateValidator<TAggregate>` - converts ZodSharp validation results into the framework's `Purview.EventSourcing.Validation.ValidationResult`
- `IAggregateValidator<TAggregate>` registration consumed by the event store during save
- A `buildTransitive` target that validates the direct `ZodSharp` reference

## Notes

- This package is optional. The core package never acquires a mandatory ZodSharp dependency.
- Validation is invoked by the event store when an aggregate is saved; failing validations produce a non-saved save result.

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)

## Related packages

- [Core package](https://github.com/purview-dev/event-sourcing/blob/main/src/src/EventSourcing/Sdk/README.md): `Purview.EventSourcing`
- [FluentValidation integration](https://github.com/purview-dev/event-sourcing/blob/main/src/src/FluentValidationImpl/Sdk/README.md): `Purview.EventSourcing.FluentValidation`