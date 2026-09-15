# Runtime Performance

The runtime performance suite (`just perf-runtime`) measures the **runtime hot paths of the code the
source generator emits**, using the generated aggregates and value objects in `src/src/Samples`
(`OrderAggregate`, `CustomerAggregate`, `EmailAddress`, `Money`, `OrderStatus`, `UserDetails`, ...).
It runs under BenchmarkDotNet with `[MemoryDiagnoser]`, so every case reports both wall time and
allocated bytes per operation.

## Running

```text
just perf-runtime              # quick run (1 warmup, 3 iterations)
just perf-runtime --benchmark  # benchmark run (3 warmup, 12 iterations)
```

Equivalent: `dotnet run --project src/src/Benchmarks/Benchmarks.csproj --configuration Release -- runtime`.

Always use Release; in Debug the JIT produces meaningless numbers.

## What is measured

| Category | Cases |
| --- | --- |
| Aggregate | `new CustomerAggregate()`, `new OrderAggregate()` (per-instance registration cost) |
| Command | generated partial methods: `CreateOrder`, `ConfirmOrder`, `ShipOrder`, `CompleteOrder`, `AddLineItem`, `RegisterCustomer`, `ChangeEmail` |
| Replay | single event application, 100-event stream replay, event `GetHashCode` |
| ValueObject | scalar (`EmailAddress`, `CurrencyCode`, `OrderStatus`) and complex (`Money`, `UserDetails`) `Create`/`Hydrate`/equality/hash/compare/`ToString`/implicit conversion |
| Serialization | event payload round-trip (the reflection-based provider path), snapshot round-trip through the generated `OrderAggregateJsonConverter`, value-object round-trip through generated converters |
| Collections | `EventStoreList` add/enumerate, `EventStoreSet` add/contains/remove |
| Mapping | `IAggregateEventNameMapper.GetName` per event type |

Command and replay cases construct a fresh aggregate per invocation (measured together with the
command), which reflects the realistic hot path: an aggregate is loaded or created, then mutated.
The delta between `Command_CreateOrder` and `AggregateConstruction_Order` isolates the command cost.

## Interpreting results

Each run writes `artifacts/runtime-performance/{history,latest.json}` and compares against the
previous run. The suite fails when:

- allocated bytes per operation regress by more than 10% for any case, or
- mean wall time regresses by more than 40% for any case.

Allocations are the most reliable regression signal in a micro-benchmark: an extra event allocation,
delegate, or closure per operation shows up immediately as a byte jump. When reporting a regression,
record the machine, framework, mode, and the history file (the previous-run comparison is only
meaningful on the same machine).

## Known hotspots and findings

The suite exists to surface and track these; the numbers below are from a representative run and
should be re-measured locally.

- **Generated command methods** construct a **single** event record per invocation: the property
  `On<Property>Changing` hooks run, `OnShouldApply` is evaluated before `OnRaising`, the payload is
  re-synchronized from post-hook values, and the event is recorded via `RecordAndApply`. A
  re-introduction of a second event construction shows up as a byte jump.
- **Event application/replay is allocation-free**: `Replay_100EventStream` allocates the same bytes
  as constructing the aggregate, because applying an event is a shared-applier lookup plus a delegate
  call plus property assignments.
- **Aggregate construction is near-zero allocation**: `AggregateBase` builds a per-type static
  applier map once (open delegates shared across instances), stores unsaved events in a
  `List<(object, EventMetadata)>`, and derives `AggregateType` from a cached name. With events as
  `sealed record` payloads carrying a struct `EventMetadata` (no per-event metadata heap object),
  `new OrderAggregate()` dropped from ~2.1 KB to **~0.4 KB** and `Command_CreateOrder` from ~2.6 KB to
  **~0.8 KB**.
- **Event-name mapping is allocation-free on the hot path**: `AggregateEventNameMapper` caches names
  by CLR type, so the per-event `Type.AssemblyQualifiedName` string is no longer built on every save
  (`EventNameMapper_GetName` measures 0 bytes/op).
- **Event/snapshot payloads serialize through reflection-based System.Text.Json** (no
  source-generated `JsonSerializerContext` for events); the generated aggregate/value-object
  converters are thin wrappers over DTOs and are already reflection-free. Event `Metadata` is
  `[JsonIgnore]`d, so payloads carry no per-event metadata and provider row columns are the source of
  truth on replay.
- **SQL Server save (SQL suite)**: skipping the guaranteed-miss existence `SELECT` for brand-new
  stream rows, caching `DbContextOptions`, gating cache-key allocations on `CacheMode`, and folding
  the snapshot write into the events batch/transaction (atomic by default via
  `RequireSnapshotWrite`, with a best-effort opt-out) reduced `EventStore_Save` from ~16 ms to
  **~12 ms**. The `EventStore_Save_NoSnapshot` case isolates the default per-save snapshot
  (Interval=1) cost; operators can raise the cadence with
  `operationContext.SetSnapshotStrategy(new IntervalSnapshotStrategy<TAggregate>(N))` with no code
  change.
- **In-memory store suite**: `just perf-inmemory` measures the allocation-free reference
  implementation — save ~10 µs, cached get ~0.3 µs, and a 101-event replay ~35 µs — so provider
  overhead can be compared against a zero-I/O baseline.

The source-generator suite has its own known incremental-caching hotspot; see
`Source-Generator-Performance.md`.