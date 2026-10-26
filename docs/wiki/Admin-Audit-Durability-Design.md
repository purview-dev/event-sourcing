# Durable Admin Audit — Design

> **Status: phases 1–4 implemented. Phase 5 (retention) remains a proposal.**
>
> Shipped: tenant and correlation stamped centrally on every entry; durable loggers for **SQL Server,
> PostgreSQL, Azure Table Storage and MongoDB** with the failure-mode option; and the read side,
> `IAdminAuditEntryReader`, with a tenant-filtered implementation for each of those four plus the
> in-memory sink. See [Guarantees and Limitations](Guarantees-and-Limitations.md#admin-audit).
>
> Three design changes against the plan below:
>
> - The stamping decorator is wired through a keyed registration (`AdminAuditKeys.Sink`) rather than
>   by decorating whatever happens to be registered. Decoration depended on the order an application
>   calls the helpers in, and broke both ways.
> - The read contract is `IAdminAuditEntryReader`, not `IAdminAuditQueryService`, returning an
>   `AdminAuditPage` with `HasMore` rather than a total count. Counting an append-only table that only
>   grows costs a scan on every page for a number that is stale before the caller reads it.
> - **There is no Admin API endpoint for it, deliberately** — decision 3 below, answered. The seam is
>   public so an application can expose it on terms it chooses; which principals may read the trail of
>   who read what is a deployment decision, not a framework default.

## The problem

In a regulated deployment the audit trail *is* the control. Ours currently dies with the process:
`Admin.Security/InMemoryAdminAuditLogger.cs` is the only implementation, so every record of who read
which event payload, who triggered a rebuild, and who erased a data subject is lost on restart — and
lost silently.

Three further gaps in the contract itself:

- **No tenant on the entry.** `AdminAuditEntry` records timestamp, feature, action, principal,
  target, success and details — but not the `TenantScope` the operation ran under. In a multi-tenant
  deployment "who did this" is incomplete without "to which tenant".
- **No read side.** `IAdminAuditLogger` has `LogAsync` only. An audit trail nobody can query is
  evidence you cannot produce.
- **No failure policy.** If the audit write fails, nothing says whether the operation should
  proceed.

## Design

### Contract changes

```csharp
public sealed record AdminAuditEntry(
    DateTimeOffset TimestampUtc,
    AdminFeature Feature,
    string Action,
    string? Principal,
    string? Target,
    bool Succeeded,
    string? Details
)
{
    /// The tenant the operation ran under; null for the system scope.
    public string? TenantId { get; init; }

    /// Correlates the audit entry with the request and with emitted events.
    public string? CorrelationId { get; init; }
}
```

Both additive, so existing construction sites keep compiling.

```csharp
public interface IAdminAuditEntryReader
{
    Task<AdminAuditPage> QueryAsync(AdminAuditQuery query, CancellationToken cancellationToken);
}
```

Kept separate from `IAdminAuditLogger` deliberately: writing and reading an audit trail are
different privileges, a deployment can grant the two to different database principals only if they
are different types, and it keeps `IAdminAuditLogger` implementable by a write-only sink — a log
pipeline, an event hub — that has no read side at all.

**`AdminAuditQuery` carries no tenant.** The tenant comes from `ICurrentTenant`, so a caller cannot
widen their own scope by asking for a different one. `PageSize` is clamped to 500: an audit table is
the one table that only grows, and an unbounded page is a way to pull the whole trail — every
principal and target it names — in a single request.

### Implementations

| Provider | Store | How the tenant is enforced |
| --- | --- | --- |
| SQL Server, PostgreSQL | A dedicated append-only table | `WHERE [TenantId] = @tenantId`, `OFFSET`/`FETCH` paging |
| Azure Storage | A table partitioned by tenant | **Structural** — the tenant *is* the partition key |
| MongoDB | A collection, indexed `(TenantId, TimestampUtc desc)` | A filter on `TenantId` |
| In-memory | The default sink | The same rule, so a test against it exercises the real boundary |

Azure Storage is the one worth calling out: structural isolation is preferred wherever the store
allows it, because a partition a query never names cannot be returned by accident, whereas a
predicate is something a future query can forget. Its row key is **inverted ticks**, so Table
Storage's own ascending order *is* newest-first within a tenant and no sort is needed — plus a random
suffix, because two entries written inside one tick would otherwise collide and `AddEntity` would
fail the second, losing an entry under the failure mode meant to be the safest.

**Every implementation fails closed.** An entry with no `TenantId` is visible only to
`TenantScope.System` — never treated as visible to everyone. One missed stamp must look like absent
data, not like another tenant's audit trail.

### Known limitation — Azure Storage system-scope reads

Table Storage orders by partition key then row key, so a cross-partition read is not globally
newest-first and the reader sorts the fetched window in memory. Bound such a read with
`FromUtc`, which the inverted row key turns into a server-side range. A tenant-scoped read needs none
of this: its single partition is already in time order. The SQL and MongoDB readers order in the
store for both scopes.

Append-only is enforced by **not offering** update or delete on the contract, and by documenting the
database grants the table expects (`INSERT`/`SELECT` only for the application principal). The
framework cannot enforce the grant; it can state it.

### Failure policy

An explicit option, because both answers are defensible and the choice is the deployment's:

```csharp
public enum AdminAuditFailureMode
{
    /// The operation proceeds; the failure is logged. Availability over evidence.
    Continue = 0,

    /// The operation is refused. Evidence over availability - the regulated default.
    Fail = 1,
}
```

Default `Fail` for privileged, mutating features (rebuild, erase); `Continue` for reads. A
deployment can override both.

### Retention

Audit entries are themselves subject to retention — typically a *minimum* retention, which is the
opposite of the event-stream problem in [Retention](Retention-Design.md). The MongoDB TTL index and
a scheduled cleanup for the SQL providers both need a floor that cannot be configured below the
deployment's policy.

## Tamper evidence

Worth deciding explicitly rather than implying. Options, cheapest first:

1. **Append-only grants plus provider-native immutability** (Azure immutable storage, SQL temporal
   tables). No framework work; strongest when the platform supports it.
2. **Hash chaining** — each entry carries the hash of its predecessor, so deletion or alteration is
   detectable on verification. Modest cost, needs a verification API and a per-tenant chain head.
3. **External sink** — write to a SIEM and treat the local table as a cache. Strongest, but moves
   the problem outside the framework.

Recommendation: ship (1), design the schema so (2) can be added without a migration (reserve a
`PreviousHash` column), and document (3) as the pattern for anyone who needs it.

## Phases

| Phase | Work |
| --- | --- |
| 1 | `TenantId` and `CorrelationId` on `AdminAuditEntry`; populate from the request scope |
| 2 | SQL Server + PostgreSQL durable loggers, with the failure-mode option |
| 3 | **Done.** `IAdminAuditEntryReader` with tenant-filtered implementations. No Admin API endpoint, by decision |
| 4 | **Done.** Azure Storage + MongoDB loggers *and* readers |
| 5 | Retention floor and cleanup — **not implemented** |

## Testing

- **Tenancy first**: an entry written under tenant A is not returned to tenant B's query; the system
  scope sees both.
- The audit write happens even when the audited operation *fails* — a refused operation is the one
  most worth recording.
- `AdminAuditFailureMode.Fail` refuses the operation when the audit store is unavailable; `Continue`
  does not. This needs the fault injection from [Resilience Testing](Resilience-Testing-Design.md).
- Reading the audit trail requires its own permission, and the deny-by-default provider refuses it.
- No code path updates or deletes an entry — asserted structurally, by the contract having no such
  method.

## Decisions

1. ~~Default **failure mode** for mutating features.~~ **Answered: `Fail`, for every feature.** An
   action nobody can prove happened is worse than an action that did not; `Continue` is available and
   logs the gap it leaves.
2. **Hash chaining: reserved, not shipped.** Every store reserves a `PreviousHash` column or field so
   it can be added without rewriting what is already stored. Until it ships, tamper-evidence rests on
   the grants a deployment issues.
3. ~~Whether the audit query endpoint is part of the Admin API.~~ **Answered: deliberately left out.**
   `IAdminAuditEntryReader` is public and registered, so an application can expose it; the framework
   does not decide who may read the record of who read what. Adding an endpoint later is additive.
4. **Still open.** Whether audit entries are tenant- or system-scoped when a super-user acts across
   tenants. The implementation records the tenant acted *upon* — that is what the central stamping
   does — but the principal's own scope is not separately recorded.

## What is still missing

- **Retention (phase 5).** Nothing deletes an audit entry, on any provider, ever. That is safe by
  default and unbounded by default. The floor it needs is a *minimum* retention, the opposite of the
  event-stream problem in [Retention](Retention-Design.md): the risk is deleting too early, not
  growing too large. Until it ships, pruning the audit table is a database-administration task, and
  the framework deliberately offers no delete path that could be mistaken for a sanctioned one.
- **Hash chaining**, as above.
