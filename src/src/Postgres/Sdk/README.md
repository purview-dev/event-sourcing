# Purview.EventSourcing.Postgres

`Purview.EventSourcing.Postgres` provides PostgreSQL event-stream persistence and an optional PostgreSQL queryable snapshot store for Purview Event Sourcing.

## Install

```bash
dotnet add package Purview.EventSourcing.Postgres
```

## Register the providers

```csharp
builder.Services.AddPostgresEventStore();
builder.Services.AddPostgresSnapshotQueryableEventStore();
```

```json
{
  "ConnectionStrings": {
    "eventstore-postgres": "Host=localhost;Database=MyApp;Username=postgres;Password=postgres"
  }
}
```

## What it provides

- Event-stream persistence for aggregates loaded through `IEventStore`
- Optional query/list/count snapshot-backed reads through `IQueryableEventStore`
- Internal event-store snapshots to support replay optimization strategies in append-only streams
- SQL-specific transaction factory (`IPostgresEventStoreTransactionFactory`) for enlisting additional SQL/EF work in the same commit (all enlisted stores must share the same SQL transaction boundary)
- Separate PostgreSQL configuration binding for event and snapshot stores
- Entity Framework-backed schema creation and CRUD paths
- JSONB-backed event and snapshot payload storage
- Shared-table safety: when aggregate types share a table, event-stream reads and deletes are scoped by both aggregate id and aggregate type
- Tolerant replay for long-lived streams: integration-tested handling for unknown event types and schema-evolved/unappliable historical events

## JSONB payloads and querying

The snapshot payload is the fully serialized aggregate graph stored in a single **JSONB** column. EF queries run against that JSON payload, so aggregate properties remain transparent to callers.

Use regular LINQ for strongly-typed queries:

```csharp
await store.QueryAsync(a => a.ComplexTestType != null && a.ComplexTestType.Int32Property == 42);
```

Use explicit JSONB operator helpers when you want GIN-friendly containment/existence checks:

```csharp
await store.WherePayloadContainsAsync("""{"ComplexTestType":{"StringProperty":"active"}}""", new() { MaxRecords = 50 });
await store.WherePayloadHasKeyAsync("ComplexTestType", new() { MaxRecords = 50 });
```

`WherePayloadContainsAsync` maps to PostgreSQL `@>` and `WherePayloadHasKeyAsync` maps to `?`.

## Event store snapshots vs query snapshots

- The event store remains append-only and is the source of truth.
- Internal snapshots inside the event store are used only to optimize replay based on snapshot strategy.
- Those internal snapshots are not a substitute for the query store and are not intended for ad-hoc querying.
- The queryable snapshot store is optional.
- If you do need a dedicated query/read store, it can be PostgreSQL via `AddPostgresSnapshotQueryableEventStore()`, or a different read technology in your application architecture.

## Declaring JSON path indexes

Configure GIN and expression indexes via options:

```csharp
options.JsonIndexOptions = new PostgresJsonIndexOptions
{
    Enabled = true,
    UseJsonbPathOps = true,
    PathIndexes =
    [
        new() { Path = "ComplexTestType.StringProperty", IndexName = "ix_snapshots_status" }
    ]
};
```

Supported members are writable primitives, `[Scalar]` value objects, complex objects composed of supported members, and `EventStoreList<T>` / `EventStoreSet<T>` collections of supported primitive or complex members.

Important query distinction:

- A `[Scalar]` value object with a primitive inner value is generally query-friendly.
- A `[Scalar]` value object with a complex inner value is persisted correctly, but deep predicates through `.Value` are not guaranteed to translate in SQL snapshot queries.
- If deep SQL predicates are required for a complex concept, expose the underlying complex type directly on the aggregate/query snapshot model (for example, a `ParserReportSummary` mirror property) and verify the exact nested predicate with integration tests.

Unsupported shapes fail during model creation, including arrays and collection types other than `EventStoreList<T>` / `EventStoreSet<T>` (for example `List<T>`, `IReadOnlyList<T>`, `IEnumerable<T>`, `HashSet<T>`, `ImmutableArray<T>`), except where a provider-specific JSON conversion path is explicitly supported and tested. Read-only and `[JsonIgnore]` members are excluded from the JSON payload.

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)
- [Provider feature matrix](https://github.com/purview-dev/event-sourcing/blob/main/docs/wiki/Provider-Feature-Matrix.md)
  - Includes behavior notes/caveats (`IsDeletedAsync` missing behavior, tolerant replay, principal requirements)
  - Includes snapshot payload/query-translation guidance for scalar value objects vs directly mapped complex mirrors
