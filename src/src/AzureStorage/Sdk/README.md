# Purview.EventSourcing.AzureStorage

`Purview.EventSourcing.AzureStorage` adds Azure Table Storage and Blob Storage persistence to Purview EventSourcing.

## Install

```bash
dotnet add package Purview.EventSourcing.AzureStorage
```

## Register the provider

```csharp
builder.Services.AddAzureStorageEventStore();
```

## What it provides

- Azure Table Storage event persistence
- Azure Blob Storage support for snapshots and large event payloads
- Configuration binding for the Azure Storage-backed event store

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)
- [Provider feature matrix](https://github.com/purview-dev/eventsourcing/blob/main/docs/wiki/Provider-Feature-Matrix.md)
