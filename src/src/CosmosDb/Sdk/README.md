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
- Azure Cosmos DB persistence for aggregate read models, mapped by Entity Framework
- Paging through native Cosmos DB continuation tokens
- Configuration binding for the Cosmos DB snapshot store, including the container indexing policy

## Snapshot shape

Snapshot documents are the aggregate itself: the mapped members sit at the document root, `id` carries
`Details.Id`, and `AggregateType` is the partition key. The mapping is shared with the SQL Server and PostgreSQL
snapshot stores, so the same member shapes are supported everywhere - primitives, `[Scalar]` value objects, complex
types, and `EventStoreList<T>`/`EventStoreSet<T>` collections of those. Members that cannot be mapped structurally
(dictionaries, read-only collections) are reported at startup; mark them `[EFOpaque]` to persist them as converted
JSON instead.

Writing a snapshot costs one additional request, because Entity Framework has no Cosmos DB upsert and the store
establishes whether the document already exists before writing it.

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)
- [Provider feature matrix](https://github.com/purview-dev/event-sourcing/blob/main/docs/wiki/Provider-Feature-Matrix.md)

Snapshot query translation capabilities differ by provider; consult the provider matrix before relying on deep nested predicates for complex value-object shapes.
