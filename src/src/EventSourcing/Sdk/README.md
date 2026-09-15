# Purview.EventSourcing

`Purview.EventSourcing` is the core Purview EventSourcing package. It provides aggregate base types, event metadata, provider-agnostic store facades, transaction coordination, and dependency injection extensions for event-sourced .NET applications.

## Install

```bash
dotnet add package Purview.EventSourcing
```

## What is included

- `AggregateBase` and related aggregate infrastructure
- `IEventStore` and `IQueryableEventStore`
- `IEventStoreTransactionFactory` and transaction result types
- Source generation for aggregate events and command methods
- Source-analysis diagnostics and code fixes (`Purview.EventSourcing.SourceGenerator` for analyzers
  and generation, `Purview.EventSourcing.SourceGenerator.CodeFixes` for IDE code fixes)
- Embedded AI agent skills for aggregate design, validation, and value-object modeling
- Common extension points used by provider packages
- Dependency injection helpers for core framework services

## Typical usage

```csharp
builder.Services.AddNullQueryableEventStore();
```

```csharp
var order = await store.GetAsync<OrderAggregate>(orderId, cancellationToken)
    ?? await store.CreateAsync<OrderAggregate>(orderId, cancellationToken: cancellationToken);

order.CreateOrder(customerId);
await store.SaveAsync(order, cancellationToken);
```

## Related packages

- SQL Server (events and snapshots): `Purview.EventSourcing.SqlServer`
- PostgreSQL (events and snapshots): `Purview.EventSourcing.Postgres`
- Azure Storage (events with blob-backed snapshots/large payloads): `Purview.EventSourcing.AzureStorage`
- MongoDB (events and snapshots): `Purview.EventSourcing.MongoDB`
- Cosmos DB (snapshot only): `Purview.EventSourcing.CosmosDb`
- In-memory (local and test scenarios): `Purview.EventSourcing.InMemory`

## Documentation

- [Repository README](https://github.com/purview-dev/eventsourcing/blob/main/README.md)
- [SQL Server guide](https://github.com/purview-dev/eventsourcing/blob/main/docs/wiki/SQL-Server-Guide.md)
- [Provider feature matrix](https://github.com/purview-dev/eventsourcing/blob/main/docs/wiki/Provider-Feature-Matrix.md)

When using snapshot-backed providers, especially SQL Server, read the provider docs before assuming deep predicates through complex scalar value object `.Value` members will translate.

## Agent skill integration

When installed from NuGet, `Purview.EventSourcing` ships bundled agent skills. Consuming repositories that use `Purview.DotNetProjectSdk` get those skills copied into `.agents\skills\` before build so supported coding agents can discover framework-specific guidance automatically.

To opt out, set:

```xml
<PropertyGroup>
  <EnableAgentFolderInPackage>false</EnableAgentFolderInPackage>
</PropertyGroup>
```
