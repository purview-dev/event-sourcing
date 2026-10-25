# Shared Testing Framework

This page describes the shared provider-agnostic test framework used by the storage-provider
integration suites.

## Overview

Each storage provider (`AzureStorage`, `CosmosDb`, `MongoDB`, `Postgres`, `SqlServer`) has its own
integration test project. Instead of duplicating the same behavioural tests for every provider, the
repository defines two shared contract suites that run against every provider that advertises the
relevant capability:

- **Event-store contract suite** — runs against providers with an event store: Azure Storage,
  MongoDB, Postgres, SQL Server. (Cosmos DB has no event stream.)
- **Snapshot-store contract suite** — runs against providers with a query snapshot store: Cosmos DB,
  MongoDB, Postgres, SQL Server. (Azure Storage has no query snapshot store.)

Provider-specific behaviour (batch limits, index creation, JSON operators, query-translation
boundaries, telemetry, storage layout) lives in per-provider guard tests in each integration project.

## Layout

| Path | Purpose |
| --- | --- |
| `src/tests/SharedTestingFramework/Contracts/` | The shared contract suites. These are **not** compiled into the `SharedTestingFramework` assembly; each provider integration test project links them into its own compilation (see below). |
| `src/tests/SharedTestingFramework/Fixtures/` | Provider Testcontainers fixtures. |
| `src/tests/<Provider>.IntegrationTests/Events/` | The per-provider event-store wiring + guard tests. |
| `src/tests/<Provider>.IntegrationTests/Snapshots/` | The per-provider snapshot-store wiring + guard tests. |
| `src/tests/<Provider>.IntegrationTests/Guards/` | Provider-specific event-store guard tests. |

## How the shared suites are wired

TUnit uses compile-time discovery, so the `[Test]` methods must be discoverable from the test
assembly. The shared suites achieve this with three TUnit features:

1. `[GenerateGenericTest(typeof(PersistenceAggregate))]` on a generic test class makes TUnit
   generate a concrete test class for the supplied aggregate type.
2. `[ClassDataSource<TFixture>(Shared = SharedType.PerTestSession)]` injects the provider fixture
   (one container per test session).
3. `[InheritsTests]` picks up the `[Test]` methods declared on the shared generic base class
   (`EventStoreContractTestsBase<TAggregate>` / `SnapshotStoreContractTestsBase<TAggregate>`).

The contract sources under `Contracts/` are linked directly into each provider test project's
compilation (via `<Compile Include="..\SharedTestingFramework\Contracts\...">` links) rather than
consumed cross-assembly. This is deliberate: TUnit's `TestMetadataGenerator` reports error diagnostic
`TUNIT0999` at the inherited method's source location when an internal generation error occurs, and
Roslyn's `SourceProductionContext.ReportDiagnostic` rejects diagnostics whose location is not part of
the compilation being analyzed (surfacing as `CS8785`). With a cross-assembly base class the location
points into `SharedTestingFramework`'s sources, outside the provider compilation, so the warning is
unavoidable. Linking the sources keeps every inherited `[Test]` method inside the provider compilation
and eliminates the failure. `SharedTestingFramework` therefore no longer carries the base `[Test]`
methods at all.

Each provider test project therefore adds a small derived class such as:

```csharp
[GenerateGenericTest(typeof(PersistenceAggregate))]
[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerTestSession)]
[InheritsTests]
public sealed class EventStoreContractTests<TAggregate>(SqlServerEventStoreFixture fixture)
    : EventStoreContractTestsBase<TAggregate>
    where TAggregate : class, IAggregateTest, new()
{
    protected override IEventStoreCore<TAggregate> CreateEventStore() => fixture.CreateEventStore<TAggregate>();

    protected override IEventStoreCore<TAggregate> CreateEventStore(IAggregateChangeFeedNotifier<TAggregate>? notifier) =>
        fixture.CreateEventStore(aggregateChangeNotifier: notifier);

    protected override Task MarkEventTypesAsUnknownAsync(...) => /* provider-specific event rewrite */;
}
```

The shared base classes only exercise the public contracts (`IEventStoreCore<T>` and
`IQueryableEventStoreCore<T>`), observable state (save results, rehydrated aggregates, change-feed
notifications, event ranges, query results) and shared aggregates (`PersistenceAggregate`,
`ComplexTestType`). Provider internals are deliberately out of scope for the shared suites.

## Where each suite runs

| Suite | AzureStorage | CosmosDb | MongoDB | Postgres | SqlServer |
| --- | --- | --- | --- | --- | --- |
| Event-store contract suite | ✓ | — | ✓ | ✓ | ✓ |
| Snapshot-store contract suite | — | ✓ | ✓ | ✓ | ✓ |
| Provider guard / feature tests | ✓ | ✓ | ✓ | ✓ | ✓ |

## Adding a new provider

1. Create the integration test project (see `project-placement-defaults`), referencing
   `SharedTestingFramework` and `Samples`, and link the contract sources that the provider needs
   (the `EventStore*` and/or `SnapshotStore*` files under `Contracts/`) — see the `<Compile Include>`
   links in the existing provider test projects.
2. Add an `EventStoreContractTests` (and/or `SnapshotStoreContractTests`) derived class wired to the
   provider fixture.
3. Implement the provider-specific seams (`MarkEventTypesAsUnknownAsync` for the unknown-event test,
   `SnapshotAsync` for the snapshot suite).
4. Add guard tests for capabilities that are not part of the shared contract.

## Adding a shared test

1. Add the `[Test]` method to the relevant shared base class under `Contracts/`.
2. Add any data sources to the matching `*ContractTestData` static class.
3. The test runs automatically for every provider whose integration project links the contract
   sources and derives from the relevant base class.

## Environment notes

- Integration suites start their containers through [Purview.Containers](https://www.nuget.org/packages/Purview.Containers),
  which selects **WSL Containers** on a Windows host and **Docker** everywhere else. Either runtime
  works; nothing in the fixtures names one. Pin a backend with `PURVIEW_CONTAINERS_BACKEND=wsl` or
  `=docker` to fail loudly instead of falling back.
- Under WSL Containers a session holds an exclusive lock on its image store, so run the integration
  suites one module at a time: `just test '/*/*/*/*' --max-parallel-test-modules 1`.
- Azure Storage, Cosmos DB, PostgreSQL and SQL Server use the matching service modules. MongoDB and
  the Cosmos DB emulator have no module, so `MongoDBTestContainer` and `CosmosDbTestContainer` in
  `SharedTestingFramework` build them from the generic `ContainerBuilder` — including the MongoDB
  single-node replica set the event store's transactions need.
- The Azure/Cosmos/Mongo snapshot fixtures rely on Azurite; the SQL fixtures on a SQL Server image.
- **The Cosmos DB suite cannot pass against the `vnext` emulator.** That emulator's query engine does
  not resolve the `root` alias Entity Framework emits (`SELECT VALUE c FROM root c`) and answers every
  query with `400 SC2001 Identifier 'root' could not be resolved`, including the existence check the
  snapshot write performs. The alias is not configurable in EF. The classic emulator image accepts it,
  as the service itself does, but is heavy enough to be unreliable under a default WSL Containers
  session. Until the vnext query engine supports the alias, validate the Cosmos provider against a
  real Cosmos DB account.
- The CI pipeline runs only the unit-test tree filter; integration suites are exercised locally or
  via an opt-in run.