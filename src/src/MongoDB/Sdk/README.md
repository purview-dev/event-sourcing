# Purview.EventSourcing.MongoDB

`Purview.EventSourcing.MongoDB` provides both MongoDB event-stream persistence and MongoDB queryable snapshot persistence for Purview EventSourcing.

## Install

```bash
dotnet add package Purview.EventSourcing.MongoDB
```

## Register the providers

```csharp
builder.Services.AddMongoDBEventStore();
builder.Services.AddMongoDBSnapshotQueryableEventStore();
```

## What it provides

- MongoDB-backed event-stream persistence through `IEventStore`
- Query/list/count/first/single snapshot-backed reads through `IQueryableEventStore`
- Configuration binding for event and snapshot MongoDB stores
- Telemetry registration for MongoDB event and snapshot operations

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)
- [Provider feature matrix](https://github.com/purview-dev/eventsourcing/blob/main/docs/wiki/Provider-Feature-Matrix.md)

Snapshot query translation capabilities differ by provider; consult the provider matrix and provider-specific guides before relying on deep nested predicates for complex value-object shapes.
