# Purview.EventSourcing.FluentValidation

`Purview.EventSourcing.FluentValidation` adapts [FluentValidation](https://docs.fluentvalidation.net/) validators to the Purview Event Sourcing aggregate validation contract (`IAggregateValidator<TAggregate>`).

## Install

```bash
dotnet add package Purview.EventSourcing.FluentValidation
```

## Register the adapter

### With an explicit validator implementation

Registers both the FluentValidation validator and the aggregate-validator adapter:

```csharp
builder.Services.AddFluentValidationAdapter<OrderAggregate, OrderValidator>();
```

### When validators are already registered

Use this overload when the validator is already registered in the container (for example, via `AddValidatorsFromAssembly`). The adapter resolves `IValidator<TAggregate>` from the container:

```csharp
builder.Services.AddFluentValidationAdapter<OrderAggregate>();
```

Both overloads accept an optional `ServiceLifetime` (defaults to `Singleton`).

## Typical usage

```csharp
// The store invokes the registered IAggregateValidator<TAggregate> before saving.
await store.SaveAsync(order, cancellationToken);
```

## What it provides

- `FluentValidationAggregateValidator<TAggregate>` - converts `ValidationResult` failures into the framework's `Purview.EventSourcing.Validation.ValidationResult`
- `IAggregateValidator<TAggregate>` registration consumed by the event store during save

## Notes

- This package is optional. The core package never acquires a mandatory FluentValidation dependency.
- Validation is invoked by the event store when an aggregate is saved; failing validations produce a non-saved save result.

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)

## Related packages

- [Core package](https://github.com/purview-dev/event-sourcing/blob/main/src/src/EventSourcing/Sdk/README.md): `Purview.EventSourcing`
- [ZodSharp integration](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Validation.ZodSharp/Sdk/README.md): `Purview.EventSourcing.Validation.ZodSharp`
