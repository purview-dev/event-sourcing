# Purview.EventSourcing.Admin.AzureStorage

`Purview.EventSourcing.Admin.AzureStorage` provides Azure Storage-backed implementations of the admin portal service contracts (`IAdminAggregateQueryService`, `IAdminEventQueryService`, `IAdminProjectionService`), reading directly from the tables and blobs written by `Purview.EventSourcing.AzureStorage`.

## Install

```bash
dotnet add package Purview.EventSourcing.Admin.AzureStorage
```

## Register the adapter

```csharp
builder.Services.AddPurviewEventSourcingAdminAzureStorage();
```

The adapter resolves the `AzureStorageEventStoreOptions` from the container (the same options used by the event store itself). For example:

```csharp
builder.Services.AddAzureStorageEventStore();
builder.Services.AddPurviewEventSourcingAdminAzureStorage();
```

## Prerequisites

- The `Purview.EventSourcing.AzureStorage` event store must be registered with matching `AzureStorageEventStoreOptions` (connection string and table/blob configuration).
- The admin API and security packages must be registered and mapped (see `Purview.EventSourcing.Admin.Api`).

## What it provides

- `AzureStorageAdminAggregateQueryService` - aggregate search and detail retrieval
- `AzureStorageAdminEventQueryService` - event-range queries
- `AzureStorageAdminProjectionService` - point-in-time projection at a version or timestamp

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)

## Related packages

- [Admin abstractions](https://github.com/purview-dev/eventsourcing/blob/main/src/src/Admin.Abstractions/Sdk/README.md): `Purview.EventSourcing.Admin.Abstractions`
- [Admin API](https://github.com/purview-dev/eventsourcing/blob/main/src/src/Admin.API/Sdk/README.md): `Purview.EventSourcing.Admin.Api`