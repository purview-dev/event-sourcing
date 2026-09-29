# Purview.EventSourcing.SqlServer

`Purview.EventSourcing.SqlServer` provides both SQL Server/Azure SQL event-stream persistence and SQL-backed queryable snapshot persistence for Purview Event Sourcing.

## Install

```bash
dotnet add package Purview.EventSourcing.SqlServer
```

## Register the providers

```csharp
builder.Services.AddSqlServerEventStore();
builder.Services.AddSqlServerSnapshotQueryableEventStore();
```

```json
{
  "ConnectionStrings": {
    "eventstore-sqlserver": "Server=.;Database=MyApp;Trusted_Connection=True;"
  }
}
```

## What it provides

- Event-stream persistence for aggregates loaded through `IEventStore`
- Query/list/count snapshot-backed reads through `IQueryableEventStore`
- SQL-specific transaction factory (`ISqlServerEventStoreTransactionFactory`) for enlisting additional SQL/EF work in the same commit (all enlisted stores must share the same SQL transaction boundary)
- SQL Server and Azure SQL configuration binding for both event and snapshot stores
- Entity Framework-backed schema creation and CRUD paths
- JSON-column-backed event and snapshot payload storage
- Shared-table safety: when aggregate types share a table, event-stream reads and deletes are scoped by both aggregate id and aggregate type
- Tolerant replay for long-lived streams: integration-tested handling for unknown event types and schema-evolved/unappliable historical events

## Payload shape

The snapshot payload is the fully serialized aggregate graph stored in a single JSON column. EF queries run against that JSON payload, so aggregate properties remain transparent to callers.

Supported members are writable primitives, `[Scalar]` value objects, complex objects composed of supported members, and `EventStoreList<T>` / `EventStoreSet<T>` collections of supported primitive or complex members.

Important query distinction:

- A `[Scalar]` value object with a primitive inner value is generally query-friendly.
- A `[Scalar]` value object with a complex inner value is persisted correctly, but deep predicates through `.Value` are not guaranteed to translate in SQL snapshot queries.
- If deep SQL predicates are required for a complex concept, expose the underlying complex type directly on the aggregate/query snapshot model (for example, a `ParserReportSummary` mirror property) and verify the exact nested predicate with integration tests.

Unsupported shapes fail during model creation, including arrays and collection types other than `EventStoreList<T>` / `EventStoreSet<T>` (for example `List<T>`, `IReadOnlyList<T>`, `IEnumerable<T>`, `HashSet<T>`, `ImmutableArray<T>`), except where a provider-specific JSON conversion path is explicitly supported and tested, or where the member is marked `[EFOpaque]`. Read-only and `[JsonIgnore]` members are excluded from the JSON payload.

Value-type (struct) value objects mapped into the payload must be materializable by EF:

- Declare a parameterless constructor (the `Purview.ValueObjects` generator emits one for `[ValueObject]` structs) or use only scalar constructor parameters. EF cannot bind complex or collection constructor parameters in JSON payloads and reports `EVENTSTOREEF003` when no constructor can be used.
- Give every complex member declared on a value type an `init` or `set` accessor. EF cannot assign a read-only member of a value type and reports `EVENTSTOREEF004`. The provider rejects the shape while building the snapshot query model, so it fails with `InvalidOperationException` naming the member instead of EF's internal `ArgumentException: Expression must be writeable` at query time.

The snapshot query model writes members through their property accessors (`PropertyAccessMode.PreferProperty`), so `readonly record struct` value objects — including value objects nested inside other value objects — round-trip and remain queryable.

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)
- [SQL Server guide](https://github.com/purview-dev/event-sourcing/blob/main/docs/wiki/SQL-Server-Guide.md)
  - Includes behavior notes/caveats (`IsDeletedAsync` missing behavior, tolerant replay, principal requirements)
  - Includes snapshot payload/query-translation guidance for scalar value objects vs directly mapped complex mirrors
