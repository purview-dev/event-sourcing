# Provider Feature Matrix

This page summarizes feature availability by package so provider selection is explicit and accurate.
The capability values below are backed by executable capability definitions registered by each
provider and asserted by the `Capabilities.UnitTests` contract suite — see
[Provider Capabilities](Provider-Capabilities.md) for the runtime-queryable contract and the exact
per-registration values.

| Capability | `Purview.EventSourcing` (core) | `Purview.EventSourcing.SqlServer` | `Purview.EventSourcing.Postgres` | `Purview.EventSourcing.AzureStorage` | `Purview.EventSourcing.MongoDB` | `Purview.EventSourcing.CosmosDb` |
| --- | --- | --- | --- | --- | --- | --- |
| Aggregate/event abstractions (`AggregateBase`, `EventBase`) | Yes | Uses core | Uses core | Uses core | Uses core | Uses core |
| Provider-agnostic event facade (`IEventStore`) | Yes | SQL event store | PostgreSQL event store | Azure Table event store | MongoDB event store | Optional registration via snapshot provider |
| Provider-agnostic query facade (`IQueryableEventStore`) | Yes (interface + null implementation) | Optional SQL snapshot store | Optional PostgreSQL snapshot store | Not provided | Optional MongoDB snapshot store | Optional Cosmos snapshot store |
| Event-stream persistence | Not persistent by itself | Yes | Yes | Yes | Yes | No |
| Snapshot-backed query/list/count | Null provider only | Optional | Optional | No | Optional | Optional |
| Blob-backed snapshots / large payloads | No | No | No | Yes | No | No |
| Provider-neutral transaction guarantee | Explicit `BestEffort` or required `Atomic` | Atomic within one database boundary | Atomic within one database boundary | Best effort | Best effort | Best effort |
| Transactional outbox (atomic with events) | No | Yes (`AddSqlServerOutbox<THandler>`) | Yes (`AddPostgresOutbox<THandler>`) | No | No | No |
| Provider-specific native transaction factory | No | `ISqlServerEventStoreTransactionFactory` | `IPostgresEventStoreTransactionFactory` | No | No | No |
| Runtime-configured JSON payload indexes | No | Yes (event + snapshot stores, auto-create path) | Yes (GIN + expression indexes on event + snapshot stores) | No | No | No |
| DI registration helpers | `AddNullQueryableEventStore()` | `AddSqlServerEventStore()`, `AddSqlServerSnapshotQueryableEventStore()` | `AddPostgresEventStore()`, `AddPostgresSnapshotQueryableEventStore()` | `AddAzureStorageEventStore()` | `AddMongoDBEventStore()`, `AddMongoDBSnapshotQueryableEventStore()` | `AddCosmosDbSnapshotQueryableEventStore()` |

## Selection guidance

- Choose **SQL Server** when you need event streams and optional SQL query snapshots with SQL-native transaction coordination.
- Choose **PostgreSQL** when you need append-only PostgreSQL event streams, replay snapshots for strategy-driven rehydration, and optionally a PostgreSQL-backed query store.
- Choose **Azure Storage** when you want Azure Table event persistence with Blob support for large payloads/snapshots.
- Choose **MongoDB** when you want both event and snapshot stores on MongoDB.
- Choose **Cosmos DB** when you only need a queryable snapshot store.

## High-scale / global-production readiness

The framework is designed for **single-region, single-authoritative-store** deployment with per-aggregate-stream concurrency. This is a sound model for horizontal scale: writes to different aggregates never contend, and ordering is guaranteed per stream. The following capabilities are built in:

- **Keyset event-history paging.** `GetEventHistoryAsync` continuation tokens record the last returned aggregate version, so each page scans only the events it needs (O(page) rather than O(stream)). Legacy integer tokens remain supported.
- **Snapshot cache single-flight.** Concurrent first-reads of a cold aggregate serialize rehydration per stream and double-check the cache, preventing replay and cache-write stampedes. Optional `EventStoreOperationContext.ValidateCachedSnapshot` rejects stale cache entries against the stream version (adds one storage read per cache hit).
- **Strategy-gated snapshots.** The SQL Server/PostgreSQL same-table snapshot honors `ISnapshotStrategy` (defaults to every save, preserving historical behavior) and per-operation overrides via `SetSnapshotStrategy`, so write amplification can be tuned.
- **Concurrency retry + in-process serialization.** `ConcurrencyRetry.ExecuteAsync` retries conflicts (all provider `ConcurrencyException` types implement `IConcurrencyConflict`) with exponential backoff; `AggregateWriteLock` serializes read-modify-write work per stream within a process.
- **Conflict recognition across providers.** MongoDB duplicate-key writes now surface as `ConcurrencyException` (not `CommitException`), and the in-memory store throws on conflicting versions instead of silently dropping them.
- **Partial-replay detection.** `AggregateBase.SkippedEvents` reports events skipped during replay so callers can detect a partially reconstructed aggregate in a mixed-version fleet. `SkippedEvents` is replay-transient metadata and is **not** persisted in SQL Server/PostgreSQL EF-backed snapshot payloads; snapshot reads reconstruct stored aggregate state without replay and therefore never report skips. Check `SkippedEvents` after event-stream loads rather than relying on snapshot persistence.

### Remaining gaps for global scale (not implemented)

- **Geo-replication / multi-region writes.** There is no framework-level replication, multi-region write path, or conflict resolution. Active-active writes across regions are unsupported; route all writes for a stream to one region or use provider-native replication.
- **Hot-partition mitigation / sharding.** A stream is a single aggregate instance; a hot aggregate concentrates onto one partition in every provider. SQL Server per-aggregate-type table/schema overrides and provider-native partitioning are the available levers.
- **Snapshot query listing paging is offset-based.** Queryable store `ListAsync`/`QueryAsync` continuation is an integer skip; deep pages are O(n). Keyset conversion for arbitrary `orderBy` clauses is not implemented.
- **Event-stream aggregate listing is an unbounded scan.** `GetAggregateIdsAsync` streams ids in deterministic (ordered) form but does not support keyset resumption through the API.

See the provider guides for provider-specific scaling configuration (for example SQL Server data compression, JSON indexes, and per-type schema overrides).

## Snapshot model reminder

- Event-store snapshots are replay/rehydration optimizations for append-only streams.
- Query snapshots are explicit read/query stores used through `IQueryableEventStore`.
- Applications may use the event store without any query snapshot store at all.
- Applications may also pair one event-store provider with a different query-store provider when that better fits read requirements.

### SQL Server query translation notes

- SQL snapshot queries support predicates over JSON-mapped primitive members and supported value-object shapes.
- SQL Server can additionally create runtime-managed indexes over supported JSON scalar paths when `JsonIndexOptions.Enabled = true` and `AutoCreateTable = true`.
- For provider-converted members (for example, a `[Scalar]` value object whose inner `Value` is a complex type), deep predicates on inner members are **not SQL-translatable** (for example: `a.ReportSummary.Value.ParserDetails.FailedLines > 0`).
- The same conceptual data **can** be queried deeply when exposed as a directly mapped complex property in the snapshot graph (for example: `a.ReportSummaryScalar.ParserDetails.FailedLines > 0`, where `ReportSummaryScalar` is a `ParserReportSummary`).
- `EventStoreList<T>` / `EventStoreSet<T>` members with `[ValueObject]` struct elements are persisted via JSON conversion for compatibility; treat nested element member filtering as non-translatable unless explicitly covered by tests.
- Nested dictionary/interface-collection members cannot be structurally mapped by the SQL Server or PostgreSQL EF snapshot model. Mark non-queryable values `[EfOpaque]` to persist them as a converted JSON scalar, or remodel them as complex entry collections when their contents must be queried.
- Opaque JSON currently uses EF's supported string conversion inside the outer JSON document. This preserves round-trip values but stores the nested value as JSON text rather than a raw nested JSON token.
- Recommended pattern: query by SQL-translatable fields first, or expose a directly mapped complex mirror property when deep SQL filtering is a real requirement.

## Related docs

- [Getting Started](Getting-Started.md)
- [Guarantees and Limitations](Guarantees-and-Limitations.md)
- [Provider Capabilities](Provider-Capabilities.md)
- [Dependency Guardrails](Dependency-Guardrails.md)
- [SQL Server Guide](SQL-Server-Guide.md)
- Postgres package README: `src/src/Postgres/Sdk/README.md`
- Core package README: `src/src/EventSourcing/Sdk/README.md`
- SQL Server package README: `src/src/SqlServer/Sdk/README.md`
- Azure Storage package README: `src/src/AzureStorage/Sdk/README.md`
- MongoDB package README: `src/src/MongoDB/Sdk/README.md`
- Cosmos DB package README: `src/src/CosmosDb/Sdk/README.md`
