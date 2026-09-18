# Purview Event Sourcing Benchmarks

Performance harnesses for Purview Event Sourcing, hosted in a single `net10.0` console application
built on **BenchmarkDotNet**. Three suites measure the framework's high-impact areas:

- **`source-generator`** — how fast the aggregate and value-object generators run and how well the
  incremental pipeline caches (cold generation vs. warm rerun vs. single-aggregate edit).
- **`runtime`** — the runtime hot paths of the code the generator emits: aggregate construction,
  command methods, event application/replay, value objects, serialization, collections, and
  event-name mapping. Reports wall time **and allocations per operation**.
- **`sql-server`** — event-store save/get and snapshot write/query timings against a SQL Server
  Testcontainer.

Each run uses the **in-process toolchain** (`InProcessEmitToolchain`), so the benchmarks execute in
the current process. This avoids BenchmarkDotNet generating an out-of-process boilerplate project,
which the repository's `Purview.BuildSdk` cannot build under. In-process runs are slightly
less isolated than out-of-process runs, but they keep the harness self-contained and fast to invoke,
and allocation measurements use `GC.GetAllocatedBytesForCurrentThread` so they remain meaningful.

Every run exports a JSON snapshot to `artifacts/` and prints a summary compared against the
previous run. `artifacts/` is not committed.

## Structure

| File | Covers |
| --- | --- |
| `Program.cs` | Mode routing (`source-generator` / `runtime` / `sql-server` / `all`), BenchmarkDotNet config and in-process job, exit codes |
| `BenchmarkPolicies.cs` | Regression thresholds and ratio policies for each suite |
| `BenchmarkRunExporter.cs` | Converts a BenchmarkDotNet `Summary` into `BenchmarkSuiteRun` JSON, applies thresholds, compares against the previous run |
| `BenchmarkRun.cs` | `BenchmarkSuiteRun` / `BenchmarkCaseRecord` models and summary formatting |
| `BenchmarkHistoryStore.cs` | Writes/reads `artifacts/{mode}-performance/{history,latest.json}` |
| `BenchmarkSuitePolicy.cs` | Policy records (case thresholds, ratio policies, regression defaults) |
| `SourceGeneratorPerformanceBenchmarks.cs` | `ColdGeneration` / `WarmRerun` / `SingleAggregateEdit` per scenario |
| `SourceGeneratorPerformanceScenarios.cs` | The five generator scenarios and the stubs they compile against |
| `RuntimePerformanceBenchmarks.cs` | Generated-code runtime hot paths with `[MemoryDiagnoser]` |
| `InMemoryPerformanceBenchmarks.cs` | In-memory store save/get/replay throughput (allocation-free reference) |
| `SqlServerPerformanceBenchmarks.cs` | SQL Server save/get/snapshot/complex-query scenarios against a Testcontainer |
| `SqlServerPerformanceWorkload.cs` | SQL Server workload dimensions |

## How to run

From the repository root (always use Release; Debug builds produce meaningless numbers):

```bash
# Source-generator harness (quick)
just perf-source-generator

# Source-generator harness (longer benchmark mode)
just perf-source-generator --benchmark

# Runtime / generated-code harness (quick)
just perf-runtime

# Runtime / generated-code harness (longer benchmark mode)
just perf-runtime --benchmark

# SQL Server harness (quick; requires Docker/Testcontainers)
just perf-sql-server

# SQL Server harness (longer benchmark mode)
just perf-sql-server --benchmark

# In-memory store harness (the allocation-free reference; quick)
just perf-inmemory

# In-memory store harness (longer benchmark mode)
just perf-inmemory --benchmark
```

Equivalent `dotnet` commands:

```bash
dotnet run --project src/src/Benchmarks/Benchmarks.csproj -c Release -- source-generator
dotnet run --project src/src/Benchmarks/Benchmarks.csproj -c Release -- runtime
dotnet run --project src/src/Benchmarks/Benchmarks.csproj -c Release -- inmemory
dotnet run --project src/src/Benchmarks/Benchmarks.csproj -c Release -- sql-server
dotnet run --project src/src/Benchmarks/Benchmarks.csproj -c Release -- all
```

Pass `--benchmark` for a longer job (3 warmup, 12 iterations instead of 1 warmup, 3 iterations).

## Results and thresholds

- Results are written to `artifacts/{mode}-performance/` (`history/` plus `latest.json`), where
  `mode` is `source-generator`, `runtime`, or `sql-server`.
- Each suite has a threshold policy in `BenchmarkPolicies.cs`:
  - **source-generator**: warm-rerun and single-edit must stay at or below 150% of cold generation
    (regression guards for the incremental pipeline), plus a 40% mean regression guard against the
    previous run.
  - **runtime**: allocations per operation must not regress more than 10% and mean time no more
    than 40% against the previous run.
  - **sql-server**: absolute per-operation millisecond thresholds (save ≤ 90 ms, get ≤ 30 ms,
    snapshot ≤ 35 ms, query ≤ 40 ms, complex query ≤ 65 ms).

A suite exits non-zero when any threshold is breached.

## Adding a new benchmark scenario

- **Source generator:** add a scenario to `SourceGeneratorPerformanceScenarios.All`; the runner
  measures baseline, cold generation, warm rerun, and single-aggregate edit automatically.
- **Runtime:** add a `[Benchmark]` method to `RuntimePerformanceBenchmarks` (optionally with
  `[BenchmarkCategory]`). Self-contained methods (fresh aggregate per invocation) measure the
  realistic hot path; pre-seeded fields avoid state coupling for value objects and serialization.
- **SQL Server:** add a `[Benchmark]` method in `SqlServerPerformanceBenchmarks` that performs an
  idempotent or append-only operation against the seeded stores, and add a matching threshold in
  `BenchmarkPolicies.SqlServer`.

See `docs/wiki/Source-Generator-Performance.md` and `docs/wiki/Runtime-Performance.md` for how to
interpret the history comparisons.