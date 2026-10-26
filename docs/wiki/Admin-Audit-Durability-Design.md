# Durable Admin Audit — Design

> **Status: proposal. Nothing on this page is implemented.**
>
> `InMemoryAdminAuditLogger` is the only `IAdminAuditLogger` that ships.

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
public interface IAdminAuditQueryService
{
    Task<PagedResult<AdminAuditEntry>> QueryAsync(AdminAuditQuery query, CancellationToken cancellationToken);
}
```

Kept separate from `IAdminAuditLogger` deliberately: writing and reading an audit trail are
different privileges, and a component that can write must not automatically be able to read.

### Implementations

| Provider | Store |
| --- | --- |
| SQL Server, PostgreSQL | A dedicated append-only table |
| Azure Storage | A table, partitioned by tenant and day |
| MongoDB | A collection with a TTL index for retention |

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
| 3 | `IAdminAuditQueryService` and an Admin API endpoint behind its own permission |
| 4 | Azure Storage + MongoDB loggers |
| 5 | Retention floor and cleanup |

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

## Decisions needed

1. Default **failure mode** for mutating features. (Recommendation: `Fail`.)
2. Whether **hash chaining** is in scope now or reserved for later.
3. Whether the audit query endpoint is part of the Admin API or deliberately left out, so audit can
   only be read through the database.
4. Whether audit entries are **tenant-scoped or system-scoped** when a super-user acts across
   tenants — my assumption is the entry records the tenant acted *upon*, with the principal's own
   scope in `Details`.
