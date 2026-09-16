# Purview EventSourcing Wiki

This wiki is the project documentation hub for framework features, provider capabilities, and release workflow.

## Start here

- [Getting Started](Getting-Started.md)
- [Guarantees and Limitations](Guarantees-and-Limitations.md)
- [Solution Design Guide](Solution-Design-Guide.md)
- [Solution Design Worksheet](Solution-Design-Worksheet.md)
- [Provider Feature Matrix](Provider-Feature-Matrix.md)
- [Provider Capabilities](Provider-Capabilities.md)
- [Transactional Outbox](Transactional-Outbox.md)
- [Transaction Guarantees](Transaction-Guarantees.md)
- [Event Contract Manifest](Event-Contract-Manifest.md)
- [Event Versioning Strategy](Event-Versioning-Strategy.md)
- [Event Versioning Examples](Event-Versioning-Examples.md)
- [Source Generator Performance](Source-Generator-Performance.md)
- [Runtime Performance](Runtime-Performance.md)
- [Dependency Guardrails](Dependency-Guardrails.md)
- [Source Generator Behaviors](Source-Generator-Behaviors.md)
- [Source Generator Code Fixes](Code-Fixes.md)
- [SQL Server Guide](SQL-Server-Guide.md)
- [Shared Testing Framework](Shared-Testing-Framework.md)
- [Release Flow](Release-Flow.md)

## Feature highlights

- **Core framework (`Purview.EventSourcing`)**
  - `AggregateBase`, `IEventStore`, `IQueryableEventStore`, and `IEventStoreTransactionFactory`.
  - [Transaction guarantees](Transaction-Guarantees.md): atomic requirements, best-effort fallback, and failure
    behavior.
  - [Snapshot schema versioning](Snapshot-Schema-Versioning.md): compatibility detection and event-replay rebuilds.
  - Source-generated aggregate events/command wiring from partial methods.
  - Provider-agnostic aggregate load/save/query APIs.
- **Solution design**
  - Paper-first worksheets for aggregate boundaries, commands, events, relationships, and event streams.
  - Guidance for relational data, value objects, validation layers, and schema evolution.
- **Storage providers**
  - SQL Server / Azure SQL: append-only event streams, internal replay snapshots, and optional SQL query snapshots with
    transaction coordination.
  - PostgreSQL: append-only event streams, internal replay snapshots, and optional PostgreSQL JSONB query snapshots.
  - Azure Storage: table-backed event streams with blob support for snapshots/large payloads.
  - MongoDB: event streams plus an optional MongoDB query snapshot store.
  - Cosmos DB: optional query snapshot store.
  - In-memory provider: non-persistent event/snapshot store for local/test scenarios.
  - Validation adapters: FluentValidation and ZodSharp adapters for `IAggregateValidator<T>`.
  - SQL snapshot translation distinguishes between provider-converted scalar value objects and directly mapped complex
    snapshot graphs; see the provider matrix and SQL guide for details.
- **Generator behavior**
  - `[Aggregate]` supports no base, direct `AggregateBase`, and transitive base-chain inheritance.
  - Property hooks are property-scoped across generated events that map that property.
  - `On<Property>Changed` runs in `Apply(...)` (including replay); `On<Property>Changing` runs on command/event-raise
    path only.
  - Event hooks (`OnRaising...`, `OnRaised...`, `OnApplied...`) are event-scoped.
