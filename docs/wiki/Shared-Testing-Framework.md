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

    protected override IEventStoreCore<TAggregate> CreateTenantScopedEventStore(TenantScope scope, Guid runId) =>
        fixture.CreateEventStore<TAggregate>(runId: runId, currentTenant: new EventStoreTenantScopeStub(scope));
}
```

The shared base classes only exercise the public contracts (`IEventStoreCore<T>` and
`IQueryableEventStoreCore<T>`), observable state (save results, rehydrated aggregates, change-feed
notifications, event ranges, query results) and shared aggregates (`PersistenceAggregate`,
`ComplexTestType`). Provider internals are deliberately out of scope for the shared suites.

## Tenant isolation is part of the contract

The event-store contract suite owns the tenancy tests: one tenant cannot read another's aggregate,
the owning tenant can, the system scope reads across all of them, a cached aggregate is not served
across tenants, and an unowned aggregate is invisible to a tenant.

The seam those tests use is **abstract**, not virtual:

```csharp
protected abstract IEventStoreCore<TAggregate> CreateTenantScopedEventStore(TenantScope scope, Guid runId);
```

A provider cannot be added without stating how it is scoped — there is no default that quietly opts
out. `runId` is shared across the two scopes in a test on purpose: both stores must address the
**same** physical storage, because sharing storage is what the isolation is protecting. A provider
that returns an isolated store per scope will pass the tests while proving nothing.

`EventStoreTenantScopeStub` (an `ICurrentTenant` fixed to one scope) is named to match the
`Contracts\EventStore*.cs` link glob, so it travels with the contract suite.

## Acceptance suite

`src/tests/EventSourcing.AcceptanceTests` states the guarantees the framework makes as
[BDTest](https://github.com/thomhurst/BDTest) scenarios, in domain language:

```csharp
await Given(() => Steps.AnAggregateExistsForTenant("acme", 5))
    .And(() => Steps.TheTenantReadsTheAggregateTwice("acme"))
    .When(() => Steps.TheTenantReadsTheAggregate("globex"))
    .Then(() => Expect.TheAggregateIsReportedAsAbsent())
    .WithScenarioText("A cached aggregate is not served to another tenant");
```

Steps live in `Steps/EventStoreSteps.cs` (actions) and `Steps/EventStoreAssertions.cs` (outcomes),
each carrying `[StepText]` so a scenario reads as prose in the BDTest report. They are reusable: a
new scenario is assembled from existing steps rather than written from scratch.

The suite runs against the **in-memory** store, so a scenario describing a guarantee needs no
container. That store enforces the same tenant isolation and returns the same result cases as the
persistent providers. Behaviour that depends on real storage — query translation, crypto-shredding,
transactions — belongs in the provider integration suites instead.

The project's `AcceptanceTests` suffix gives it `PurviewTestType=AcceptanceTests` and the TUnit
category `Acceptance`, so PR validation's `[Category=Unit]` filter excludes it. Run it with
`just test '/*/*/*/*[Category=Acceptance]'`.

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
3. Implement the provider-specific seams (`CreateTenantScopedEventStore` for the tenancy tests,
   `MarkEventTypesAsUnknownAsync` for the unknown-event test, `SnapshotAsync` for the snapshot
   suite). Only `CreateTenantScopedEventStore` is abstract; the compiler will ask for it.
4. Add guard tests for capabilities that are not part of the shared contract.

## Adding a shared test

1. Add the `[Test]` method to the relevant shared base class under `Contracts/`.
2. Add any data sources to the matching `*ContractTestData` static class.
3. The test runs automatically for every provider whose integration project links the contract
   sources and derives from the relevant base class.

## Fault injection

`FaultInjectingEventStore<T>` wraps any `IEventStoreCore<T>` and fails chosen calls, so the paths
that only run when storage misbehaves can be tested at all. `FaultPlan` says which call fails and
how:

```csharp
FaultPlan.None.FailAfter(FaultOperation.Save);   // the write lands; the caller is told it did not
FaultPlan.None.FailBefore(FaultOperation.Get, onCall: 2);
```

**`FailAfter` is the one that matters.** It models a lost response - a timeout after commit, a
connection dropped mid-acknowledgement - where storage changed and the caller believes it did not.
That is the fault that produces duplicates, because the caller's natural reaction is to retry an
operation that already happened. `FailBefore` only proves nothing was written.

Faults are scheduled explicitly by call number rather than randomly: a test that fails the third
save reproduces, where one that fails a random save turns a real defect into an intermittent
annoyance nobody trusts. Each fault fires once, so a retry after a fault can succeed.

The sources live here so provider suites can reuse them, and are **linked** into
`EventSourcing.UnitTests` rather than referenced - this project pulls in every provider and the
container packages, which would make the unit suite slow and infrastructure-dependent for the sake
of two files.

`EventStoreFailureInvariantTests` uses them against the in-memory store, so the invariants run on
every pull request with no infrastructure: events survive a lost response, a retry does not
double-apply, a failed save leaves nothing behind, versions stay contiguous across a failure, and
every one of those holds inside a tenant scope.

### Transport faults

`FaultInjectingEventStore<T>` fails at the store boundary. `FaultingTcpProxy` fails *below* it: it
sits between the client and the container and severs the connection on command, so the paths that
only run when the network misbehaves can be tested at all.

```csharp
await using var proxy = FaultingTcpProxy.Start(host, port);
proxy.ResetRepliesFromConnection = 1;   // the write lands; the reply never arrives
proxy.ResetAfterBytesSent = 256;        // a half-written request
```

Point the store's connection string at `proxy.Port` on loopback, and **disable the SDK's own connect
retries** — otherwise the test measures the retry policy rather than what the stream looks like
afterwards.

Deterministic for the same reason `FaultPlan` is: it faults the *nth* connection or after an exact
byte count, not at random. Toxiproxy was the alternative and was not taken — probabilistic toxics
turn a real defect into an intermittent annoyance, and it would add a container and a dependency.

It forwards bytes and is **not protocol-aware**: enough to sever a connection convincingly, nowhere
near enough to corrupt one in a protocol-aware way. `SqlServerTransportFaultTests` is the only suite
wired to it so far.

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