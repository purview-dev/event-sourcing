# Purview.EventSourcing.Admin.Postgres

`Purview.EventSourcing.Admin.Postgres` provides PostgreSQL-backed implementations of the admin portal service contracts (`IAdminAggregateQueryService`, `IAdminEventQueryService`, `IAdminProjectionService`), reading directly from the event and snapshot tables written by `Purview.EventSourcing.Postgres`.

## Install

```bash
dotnet add package Purview.EventSourcing.Admin.Postgres
```

## Register the adapter

```csharp
builder.Services.AddPurviewEventSourcingAdminPostgres();
```

The adapter resolves the `PostgresEventStoreOptions` from the container (the same options used by the event store itself). For example:

```csharp
builder.Services.AddPostgresEventStore();
builder.Services.AddPostgresSnapshotQueryableEventStore();
builder.Services.AddPurviewEventSourcingAdminPostgres();
```

## Prerequisites

- The `Purview.EventSourcing.Postgres` event (and ideally snapshot) stores must be registered with matching `PostgresEventStoreOptions` (connection string and schema configuration).
- The admin API and security packages must be registered and mapped (see `Purview.EventSourcing.Admin.Api`).

## What it provides

- `PostgresAdminAggregateQueryService` - aggregate search and detail retrieval over the snapshot/event tables
- `PostgresAdminEventQueryService` - event-range queries over the event table
- `PostgresAdminProjectionService` - point-in-time projection at a version or timestamp

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)

## Related packages

- [Admin abstractions](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Abstractions/Sdk/README.md): `Purview.EventSourcing.Admin.Abstractions`
- [Admin API](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.API/Sdk/README.md): `Purview.EventSourcing.Admin.Api`