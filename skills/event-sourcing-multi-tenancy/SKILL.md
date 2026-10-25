---
name: event-sourcing-multi-tenancy
description: "Use when running Purview.EventSourcing for more than one tenant - registering ICurrentTenant, the TenantScope.System infrastructure scope, which stores enforce isolation, why unowned rows fail closed, admin tenant scoping, and what to test so the boundary actually holds."
license: MIT
metadata:
  package: Purview.EventSourcing
  category: security
---

# Multi-tenancy

Tenant isolation is opt-in and **enforced by the store**, not by callers remembering to filter.

## Registering a tenant resolver

```csharp
sealed class HttpContextCurrentTenant(IHttpContextAccessor accessor) : ICurrentTenant
{
    public TenantScope Current =>
        accessor.HttpContext?.User.FindFirst("tenant")?.Value is { Length: > 0 } tenantId
            ? TenantScope.For(tenantId)
            : TenantScope.System;
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ICurrentTenant, HttpContextCurrentTenant>();
builder.Services.AddEventSourcing();   // registers its default only if none is present
```

Register **before** `AddEventSourcing()`: the framework uses `TryAddSingleton`, so the first
registration wins.

> Returning `TenantScope.System` when no tenant can be resolved, as above, grants a cross-tenant
> view. In a request pipeline prefer rejecting the request instead.

## `TenantScope.System` is not "no tenant"

`System` is the **infrastructure** scope: it crosses every tenant and applies no filter. It exists
for projection rebuilds, migrations and outbox dispatch, and it is what a single-tenant deployment
runs under by default.

It is deliberately distinct from a null tenant id, so a resolver that *fails* to determine a tenant
cannot be mistaken for permission to read everything.

Change scope temporarily with `AmbientCurrentTenant.BeginScope`:

```csharp
using (AmbientCurrentTenant.BeginScope(TenantScope.System))
    await rebuilder.RebuildAsync(cancellationToken);
```

## A tenant-less deployment needs no configuration

Register nothing: the scope stays `System`, every filter short-circuits, writes record no owner, and
behaviour is exactly as it was before tenancy existed.

## Unowned rows fail closed

A row with no `TenantId` is visible **only** to `TenantScope.System`. The filters deliberately do not
treat a null owner as visible to every tenant.

That matters more than it looks. If a write path ever fails to stamp the tenant, the row becomes
invisible to its own tenant - an obvious, reportable absence - instead of silently readable by all of
them. **Fail-closed turns a stamping bug into missing data rather than a cross-tenant leak.**

The consequence: enabling tenancy over data written while tenant-less hides that data from
tenant-scoped reads. Backfill `TenantId`, or read it under `TenantScope.System`.

## What each store enforces

| Store | Mechanism |
| --- | --- |
| SQL Server, PostgreSQL | `TenantId` column, Entity Framework global query filter |
| MongoDB | `TenantId` field composed into every read and delete filter |
| Azure Storage | `TenantId` property in the OData filter; point reads filtered on read-back |
| Cosmos DB snapshots | `__tenantId` shadow property applied after `WithPartitionKey` |
| In-memory | Ownership tracked beside the data |

**The distributed cache is namespaced by tenant.** The cache is read *before* the tenant-filtered
storage lookup, so an unqualified key would let one tenant read another's aggregate straight out of a
shared cache, bypassing every filter above. If you add a cache, namespace its key the same way.

Azure Storage blob-backed snapshots are not filtered directly; they are unreachable without first
passing the tenant-filtered stream-version read.

## Admin scoping

Set `AdminPortalOptions.EnforceTenantScope` and every admin request must resolve to exactly one
tenant. `IAdminTenantResolver` decides it from the caller's entitlements and the `Purview-Tenant`
header:

| Entitlements | Tenant requested | Result |
| --- | --- | --- |
| one | none | scoped to it |
| one | a different one | `403` |
| several | none | `400` |
| several | one they hold | scoped to it |

A super user who names no tenant is **refused, not given the system scope**. The resolver returns a
tenant identifier and nothing else, so crossing tenants is unreachable from a signed-in session.

## Testing it - the part that is easy to get wrong

Every item below was a real defect found while the tenancy tests were passing.

- **Share storage between scopes.** Two stores built with different run identifiers use different
  databases, so "tenant B cannot see tenant A" passes while proving nothing. Change only the
  `ICurrentTenant`.
- **Test the cached path.** Prime the cache as the owning tenant, then assert the other tenant still
  cannot read. Per-test cache instances hide the leak entirely.
- **Assert all three outcomes**: the owner reads it, another tenant does not, and `System` does.
  Testing only the denial misses a filter that hides data from everyone.
- **Count the write paths.** A provider may construct its stored entity in several places; one that
  misses the stamp makes reads look like the filter is broken.
- **Check the filter is on the runtime context.** A filter on a migrations-only `DbContext` compiles,
  passes, and protects nothing.

A green suite is not evidence the boundary holds.
