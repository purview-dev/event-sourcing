# Purview.EventSourcing.Admin.Security

`Purview.EventSourcing.Admin.Security` provides the authorization policies and handlers that protect the Purview EventSourcing admin portal endpoints.

## Install

```bash
dotnet add package Purview.EventSourcing.Admin.Security
```

## Register the security services

```csharp
builder.Services.AddAuthorization();

// Defaults to a deny-by-default permission provider when none is supplied.
builder.Services.AddPurviewEventSourcingAdminSecurity();
```

Or supply a custom `IAdminPermissionProvider`:

```csharp
builder.Services.AddPurviewEventSourcingAdminSecurity(new MyPermissionProvider());
```

## Map the authorization policies

```csharp
builder.Services
    .AddAuthorizationBuilder()
    .AddPurviewEventSourcingAdminPolicies();
```

## Policies

`AdminPortalPolicies` defines the policy names used by the portal:

| Constant | Value | Purpose |
| --- | --- | --- |
| `SearchAggregates` | `AdminPortal.Aggregates.Search` | Searching for aggregates |
| `ViewAggregate` | `AdminPortal.Aggregates.View` | Viewing a single aggregate |
| `ViewEvents` | `AdminPortal.Events.View` | Viewing event history |
| `ViewEventPayloads` | `AdminPortal.Events.Payload.View` | Viewing serialized event payloads |
| `ProjectPointInTime` | `AdminPortal.Projections.Execute` | Point-in-time projection |
| `ExportEvents` | `AdminPortal.Events.Export` | Exporting events |

## Authorization model

- **Deny by default** - the default `DenyAllPermissionProvider` grants nothing.
- Permissions are resolved through `IAdminPermissionProvider` and represented as `AdminPermission` records (feature + optional aggregate-type scope + allow/deny).
- `AdminFeatureAuthorizationHandler` enforces feature-level access; `AggregateTypeAccessHandler` additionally scopes access to the requested aggregate type when a permission is scoped.
- `ViewEvents` grants access to event metadata. Payloads are returned as `null` unless `ViewEventPayloads` is also granted. Event export requires both `ExportEvents` and `ViewEventPayloads` so it cannot bypass payload authorization.

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)

## Related packages

- [Admin abstractions](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Abstractions/Sdk/README.md): `Purview.EventSourcing.Admin.Abstractions`
- [Admin API](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.API/Sdk/README.md): `Purview.EventSourcing.Admin.Api`
- [Admin UI](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Site/Sdk/README.md): `Purview.EventSourcing.Admin.Site`
