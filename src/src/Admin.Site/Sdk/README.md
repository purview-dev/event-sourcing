# Purview.EventSourcing.Admin.Site

`Purview.EventSourcing.Admin.Site` is an optional Razor Class Library that provides a ready-to-use web UI for the Purview Event Sourcing admin portal. It lets you browse aggregates, inspect event streams, and explore point-in-time projections through Razor Pages.

The pages talk to the Admin API through the generated typed client (`Purview.EventSourcing.Admin.Client`), so the UI is a real consumer of the Admin API contract.

## Install

```bash
dotnet add package Purview.EventSourcing.Admin.Site
```

## Register and map the UI

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register the admin services (API options, storage adapter, security).
builder.Services.AddPurviewEventSourcingAdminApi();
builder.Services.AddPurviewEventSourcingAdminSqlServer();
builder.Services.AddPurviewEventSourcingAdminSecurity();

// Register the Razor Pages UI (also registers the generated Admin API client).
builder.Services.AddPurviewEventSourcingAdminSite(enableRazorRuntimeCompilation: false);

var app = builder.Build();

// Map the admin API endpoints and the Razor Pages UI.
app.MapPurviewEventSourcingAdminAPI();
app.MapPurviewEventSourcingAdminSite(); // defaults to the /admin prefix

app.Run();
```

`MapPurviewEventSourcingAdminSite` accepts a `pathPrefix` (defaults to `/admin`).

### Client configuration

By default the pages call the Admin API on the same origin (the request's `scheme://host`), which is appropriate when the UI and the API are hosted in the same web application. To target a remote Admin API:

```csharp
builder.Services.AddPurviewEventSourcingAdminSite(
    enableRazorRuntimeCompilation: false,
    configureClient: options =>
    {
        options.BaseUrl = new Uri("https://admin.example.com");
        options.AccessToken = "optional-bearer-token";
    }
);
```

The generated client targets the Admin API route prefix from its OpenAPI document (by default `/admin/api`).

## Pages

### Index (`/admin/`)

The aggregate search page.

- Filter by aggregate type and aggregate id
- Paginated table of matching aggregates with version and last-updated information
- Navigation links to the event-history and projection pages for each aggregate

### Events (`/admin/events`)

The event-stream inspector for a single aggregate.

- Lists the event history in version order
- Displays event metadata (aggregate version, timestamp, idempotency id, user id, causation id, correlation id)
- Shows each event payload as collapsible JSON

### Projection (`/admin/projection`)

The point-in-time projection viewer.

- Project aggregate state at a specific stream version
- Project aggregate state at a specific UTC timestamp
- Shows projection provenance (applied/skipped events) and the final aggregate state as JSON

## Requirements

The UI consumes the Admin API through `Purview.EventSourcing.Admin.Client`, so the web application must also map the Admin API endpoints (`MapPurviewEventSourcingAdminAPI`) and register an admin storage adapter (for example `Purview.EventSourcing.Admin.SqlServer`).

## Authorization

Authorization is enforced at the API layer using the policies defined in `Purview.EventSourcing.Admin.Security`. Ensure the policies are registered and your `IAdminPermissionProvider` grants the relevant `AdminFeature` permissions (search, aggregate view, event view, projection, export). The UI's HTTP calls to the API are authorized the same way as any other caller.

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)

## Related packages

- [Admin abstractions](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Abstractions/Sdk/README.md): `Purview.EventSourcing.Admin.Abstractions`
- [Admin API](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.API/Sdk/README.md): `Purview.EventSourcing.Admin.Api`
- [Admin client](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Client/Sdk/README.md): `Purview.EventSourcing.Admin.Client`
- [Admin security](https://github.com/purview-dev/event-sourcing/blob/main/src/src/Admin.Security/Sdk/README.md): `Purview.EventSourcing.Admin.Security`
