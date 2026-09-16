# Purview.EventSourcing.Admin.MongoDB

`Purview.EventSourcing.Admin.MongoDB` provides MongoDB-backed implementations of the admin portal service contracts (`IAdminAggregateQueryService`, `IAdminEventQueryService`, `IAdminProjectionService`), reading directly from the collections written by `Purview.EventSourcing.MongoDB`.

## Install

```bash
dotnet add package Purview.EventSourcing.Admin.MongoDB
```

## Register the adapter

```csharp
builder.Services.AddPurviewEventSourcingAdminMongoDB();
```

By default the adapter reads from the `EventStore` database. Provide a different database name when needed:

```csharp
builder.Services.AddPurviewEventSourcingAdminMongoDB(databaseName: "MyEventStore");
```

An `IMongoClient` must be registered (for example, via `AddSingleton<IMongoClient>` or the MongoDB driver's DI support), and the `Purview.EventSourcing.MongoDB` event store should use the same database and collection naming.

## Prerequisites

- An `IMongoClient` is registered in the container.
- The `Purview.EventSourcing.MongoDB` event store writes to the same database.
- The admin API and security packages must be registered and mapped (see `Purview.EventSourcing.Admin.Api`).

## What it provides

- `MongoDbAdminAggregateQueryService` - aggregate search and detail retrieval
- `MongoDbAdminEventQueryService` - event-range queries
- `MongoDbAdminProjectionService` - point-in-time projection at a version or timestamp

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)

## Related packages

- [Admin abstractions](https://github.com/purview-dev/eventsourcing/blob/main/src/src/Admin.Abstractions/Sdk/README.md): `Purview.EventSourcing.Admin.Abstractions`
- [Admin API](https://github.com/purview-dev/eventsourcing/blob/main/src/src/Admin.API/Sdk/README.md): `Purview.EventSourcing.Admin.Api`