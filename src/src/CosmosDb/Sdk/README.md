# Purview.EventSourcing.CosmosDb

`Purview.EventSourcing.CosmosDb` adds Azure Cosmos DB queryable snapshot persistence to Purview Event Sourcing.

## Install

```bash
dotnet add package Purview.EventSourcing.CosmosDb
```

## Register the provider

```csharp
builder.Services.AddCosmosDbSnapshotQueryableEventStore();
```

## What it provides

- Query, list, count, and snapshot-backed reads through `IQueryableEventStore`
- Azure Cosmos DB persistence for aggregate read models
- Configuration binding for the Cosmos DB snapshot store

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)
- [Provider feature matrix](https://github.com/purview-dev/event-sourcing/blob/main/docs/wiki/Provider-Feature-Matrix.md)

Snapshot query translation capabilities differ by provider; consult the provider matrix before relying on deep nested predicates for complex value-object shapes.
