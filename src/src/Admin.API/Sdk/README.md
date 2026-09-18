# Purview.EventSourcing.Admin.API

`Purview.EventSourcing.Admin.API` provides minimal-API endpoints for the Purview Event Sourcing admin portal: aggregate search, aggregate details, event-history inspection, point-in-time projection, and event export.

## Install

```bash
dotnet add package Purview.EventSourcing.Admin.API
```

## Register and map the endpoints

```csharp
builder.Services.AddPurviewEventSourcingAdminAPI(options =>
{
    options.Enabled = true;
    options.RoutePrefix = "/admin/api";
    options.Features.SearchAggregates = true;
    options.Features.ViewAggregate = true;
    options.Features.ViewEvents = true;
    options.Features.ProjectPointInTime = true;
    options.Features.ExportEvents = true;
});

var app = builder.Build();

app.MapPurviewEventSourcingAdminAPI();
```

## Configuration

`AdminPortalOptions` binds from the `AdminPortal` configuration section and can also be configured inline. Key settings:

- `Enabled` - master switch; when `false`, no endpoints are mapped (default `true`)
- `RoutePrefix` - route group prefix (default `/admin/api`)
- `Features` - per-capability toggles for search, aggregate view, event history, point-in-time projection, and export
- `Paging` - `DefaultPageSize` (default `50`) and `MaxPageSize` (default `500`)
- `Projections` - `MaxVersionsPerQuery` (default `10000`) and `MaxTimeRangePerQuery` (default 365 days)

```json
{
  "AdminPortal": {
    "Enabled": true,
    "RoutePrefix": "/admin/api",
    "Features": {
      "SearchAggregates": true,
      "ViewAggregate": true,
      "ViewEvents": true,
      "ProjectPointInTime": true,
      "ExportEvents": false
    }
  }
}
```

## Endpoints

### Host authorization and endpoint conventions

The built-in policies remain the defaults, but a host can require its own policy for any feature and apply standard endpoint conventions such as rate limiting, CORS, tags, or additional metadata:

```csharp
app.MapPurviewEventSourcingAdminAPI(configureEndpoints: endpoints =>
{
    endpoints.RequirePolicy(AdminFeature.ViewEvents, "OperationsEventReader");
    endpoints.GroupConvention = group => group.RequireRateLimiting("admin");
    endpoints.EndpointConvention = (feature, endpoint) =>
        endpoint.WithMetadata(new AdminAuditMetadata(feature));
});
```

The named policies must be registered by the host through ASP.NET Core authorization. The framework's default feature policies continue to use `IAdminPermissionProvider` and deny access unless permissions are explicitly granted.

All endpoints are grouped under `RoutePrefix` and require authorization:

- Search aggregates: `POST /aggregates/search`
- Aggregate details: `GET /aggregates/{aggregateType}/{aggregateId}`
- Event range: `GET /aggregates/{aggregateType}/{aggregateId}/events`
- Project at version: `GET /aggregates/{aggregateType}/{aggregateId}/projection?version={version}`
- Project at time: `GET /aggregates/{aggregateType}/{aggregateId}/projection/time?asOfUtc={utcTimestamp}`
- Export events: `GET /aggregates/{aggregateType}/{aggregateId}/events/export` (JSON Lines, `application/x-ndjson`)

### Request validation

Request contracts (`AggregateSearchRequest`, `EventRangeRequest`) are validated with source-generated ZodSharp schemas driven by DataAnnotations. Invalid requests return RFC 7807 `application/problem+json` with `400 Bad Request`. Validation covers:

- `Page` and `PageSize` must be positive; `PageSize` is clamped to `AdminPagingOptions.MaxPageSize`
- `VersionFrom`/`VersionTo` must be positive and `VersionFrom <= VersionTo` when both are present
- `FromUtc`/`ToUtc` must satisfy `FromUtc <= ToUtc` when both are present
- `Sort` must match a `field asc|desc` shape
- Projection `asOfUtc` must be a UTC timestamp (zero offset)

`404 Not Found` is returned only when the aggregate stream does not exist; malformed input returns `400`.

## OpenAPI

The Admin API exposes a dedicated OpenAPI document for typed-client generation:

```csharp
builder.Services.AddPurviewEventSourcingAdminOpenAPI();
app.MapOpenApi(); // /openapi/admin.json
```

The document contains only the Admin API paths, declares a global bearer security requirement, and is consumed by the generated `Purview.EventSourcing.Admin.Client` package (NSwag).

## Dependencies

You must also register:

- An admin storage adapter (for example `Purview.EventSourcing.Admin.SqlServer`) that provides the `IAdminAggregateQueryService`, `IAdminEventQueryService`, and `IAdminProjectionService` implementations.
- The authorization policies defined in `Purview.EventSourcing.Admin.Security`.

Request validation uses [ZodSharp](https://github.com/RemiBou/ZodSharp); `ZodSharp`, `ZodSharp.AspNetCore`, and `ZodSharp.SystemTextJson` are direct dependencies of this package.

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)

## Related packages

- [Admin abstractions](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Abstractions/Sdk/README.md): `Purview.EventSourcing.Admin.Abstractions`
- [Admin client](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Client/Sdk/README.md): `Purview.EventSourcing.Admin.Client`
- [Admin security](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Security/Sdk/README.md): `Purview.EventSourcing.Admin.Security`
- [Admin UI](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Site/Sdk/README.md): `Purview.EventSourcing.Admin.Site`
- Storage adapters: `Purview.EventSourcing.Admin.SqlServer`, `Purview.EventSourcing.Admin.MongoDB`, `Purview.EventSourcing.Admin.Postgres`, `Purview.EventSourcing.Admin.AzureStorage`
