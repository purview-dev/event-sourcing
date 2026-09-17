# Purview.EventSourcing.Admin.Abstractions

`Purview.EventSourcing.Admin.Abstractions` defines the provider-neutral contracts and response models shared by the Purview EventSourcing admin portal packages. Storage adapters (SQL Server, MongoDB, Postgres, Azure Storage) and the API/UI layers depend on this package.

## Install

```bash
dotnet add package Purview.EventSourcing.Admin.Abstractions
```

## What is included

- **Service contracts** - the interfaces storage adapters implement:
  - `IAdminAggregateQueryService` - aggregate search and detail retrieval
  - `IAdminEventQueryService` - event-range queries
  - `IAdminProjectionService` - point-in-time projections
  - `IAdminPermissionProvider` - user-permission resolution (deny by default)
- **Request/response models**:
  - `AggregateSummaryResponse`, `EventEnvelopeResponse`, `EventMetadataResponse`, `ProjectionResponse`, `ProjectionProvenance`
  - `PagedResult<T>` and query records (`AggregateSearchQuery`, `EventRangeQuery`)
  - `AdminFeature` and `AdminPermission`

## Typical usage

This package is normally consumed transitively by a storage adapter and the admin UI/API packages. A custom storage adapter implements the service contracts and is registered against them:

```csharp
builder.Services.AddTransient<IAdminAggregateQueryService, MyAdminAggregateQueryService>();
```

## Authorization model

Permissions are expressed as `AdminPermission` records combining an `AdminFeature`, an optional aggregate-type scope, and an allow/deny flag. The `IAdminPermissionProvider` contract is intentionally deny-by-default: a feature is only authorized when a matching allow permission exists.

Event history uses separate `ViewEvents` and `ViewEventPayloads` permissions. Metadata remains available to event-history readers, while `EventEnvelopeResponse.Payload` is `null` for callers without payload access.

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)

## Related packages

- [Admin API](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.API/Sdk/README.md): `Purview.EventSourcing.Admin.Api`
- [Admin security](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Security/Sdk/README.md): `Purview.EventSourcing.Admin.Security`
- [Admin UI](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Site/Sdk/README.md): `Purview.EventSourcing.Admin.Site`
- [Provider feature matrix](https://github.com/purview-dev/event-sourcing/blob/main/docs/wiki/Provider-Feature-Matrix.md)
