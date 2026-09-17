# Purview.EventSourcing.Admin.SqlServer

`Purview.EventSourcing.Admin.SqlServer` provides SQL Server-backed implementations of the admin portal service contracts (`IAdminAggregateQueryService`, `IAdminEventQueryService`, `IAdminProjectionService`), reading directly from the event and snapshot tables written by `Purview.EventSourcing.SqlServer`.

## Install

```bash
dotnet add package Purview.EventSourcing.Admin.SqlServer
```

## Register the adapter

```csharp
builder.Services.AddPurviewEventSourcingAdminSqlServer();
```

The adapter resolves the `SqlServerEventStoreOptions` from the container (the same options used by the event store itself). For example:

```csharp
builder.Services.AddSqlServerEventStore();
builder.Services.AddSqlServerSnapshotQueryableEventStore();
builder.Services.AddPurviewEventSourcingAdminSqlServer();
```

## Prerequisites

- The `Purview.EventSourcing.SqlServer` event (and ideally snapshot) stores must be registered with matching `SqlServerEventStoreOptions` (connection string and schema configuration).
- The admin API and security packages must be registered and mapped (see `Purview.EventSourcing.Admin.Api`).

## What it provides

- `SqlServerAdminAggregateQueryService` - aggregate search and detail retrieval over the snapshot/event tables
- `SqlServerAdminEventQueryService` - event-range queries over the event table
- `SqlServerAdminProjectionService` - point-in-time projection at a version or timestamp

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)

## Related packages

- [Admin abstractions](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Abstractions/Sdk/README.md): `Purview.EventSourcing.Admin.Abstractions`
- [Admin API](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.API/Sdk/README.md): `Purview.EventSourcing.Admin.Api`
- [SQL Server guide](https://github.com/purview-dev/event-sourcing/blob/main/docs/wiki/SQL-Server-Guide.md)