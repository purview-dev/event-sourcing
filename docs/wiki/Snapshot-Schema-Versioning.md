# Snapshot Schema Versioning

Snapshots are replaceable optimizations. Event streams remain the source of truth.

When an aggregate change makes older serialized snapshots unsafe to read, declare a new snapshot schema version:

```csharp
[SnapshotSchemaVersion(2)]
[Aggregate]
public sealed partial class Order : AggregateBase
{
}
```

The default version is 1, so existing aggregates and storage names remain compatible. Versions must be positive and
are inherited by derived aggregate types.

SQL Server and PostgreSQL store the version on the snapshot row. MongoDB stores it on the snapshot document. Azure
Storage uses a versioned blob name. Distributed-cache keys are also versioned. A mismatch is detected before payload
deserialization; the store ignores the snapshot and rebuilds the aggregate from its complete event stream. The next
snapshot-eligible save writes the current schema and replaces or supersedes the incompatible snapshot.

This fallback is safe because it never mutates the event stream and never treats a snapshot as canonical state. A
version bump may temporarily increase replay work, so deploy it before removing runtime types or converters needed by
old snapshot payloads if a rolling deployment must support both application versions.

## Cosmos DB document shape change

The Cosmos DB snapshot store now maps documents with Entity Framework rather than serializing the aggregate with
`System.Text.Json`. Documents written by an earlier version do not match the new model and are not read back. Because
snapshots are replaceable, the migration is a re-snapshot: delete the container's documents (or the container) and let
snapshot-eligible saves rewrite them, or rebuild them from the event stream through the Admin portal. No event data is
affected.

## Administrative inspection and rebuild

The Admin portal reports whether a snapshot is materialized for an aggregate
(`GET /admin/api/aggregates/{aggregateType}/{aggregateId}/snapshot`, opt-in via `ViewSnapshot`) and can
reconstruct a snapshot from the canonical event stream
(`POST /admin/api/aggregates/{aggregateType}/{aggregateId}/snapshot/rebuild`, opt-in, separately
authorized, and audited via `RebuildSnapshot`). Rebuild is idempotent and requires both an
event-backed `IEventStore` and a registered `IQueryableEventStore`; it never mutates the event
stream.
