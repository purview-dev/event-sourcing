# Purview EventSourcing Benchmarks

Performance harnesses for Purview EventSourcing. This is a `net10.0` console application that hosts
two independent harnesses:

- **Source-generator harness** — measures how fast the aggregate and value-object generators run and,
  more importantly, how well their incremental pipeline caches work.
- **SQL Server harness** — measures event-store save/get and snapshot write/query timings against a
  SQL Server Testcontainer.

Each run writes a JSON snapshot to `artifacts/` and prints a summary compared against the previous
run. `artifacts/` is not committed.

## Structure

| File | Covers |
| --- | --- |
| `SourceGeneratorPerformanceRunner.cs` | Runs the generator scenarios and enforces the warm-rerun / single-edit regression thresholds |
| `SourceGeneratorPerformanceScenarios.cs` | Aggregate and value-object generator scenarios plus the compiled-in stubs they compile against |
| `PerformanceModels.cs` | Source-generator run/scenario result models and summary formatting |
| `PerformanceHistoryStore.cs` | Writes/reads `artifacts/source-generator-performance/{history,latest.json}` |
| `SqlServerStorePerformanceRunner.cs` | SQL Server save/get/snapshot/complex-query scenarios against a Testcontainer |
| `SqlServerStorePerformanceScenarioRun.cs` | SQL Server scenario result model and threshold check |
| `SqlServerStorePerformanceRun.cs` | SQL Server run model and summary formatting |
| `SqlServerStorePerformanceHistoryStore.cs` | Writes/reads `artifacts/sqlserver-performance/{history,latest.json}` |
| `SqlServerPerformanceWorkload.cs` | SQL Server workload dimensions (aggregate count, events per aggregate, query iterations) |

## How to run

From the repository root:

```bash
# Source-generator harness (quick)
just perf-source-generator

# Source-generator harness (longer benchmark mode)
just perf-source-generator --benchmark

# SQL Server harness (quick; requires Docker/Testcontainers)
just perf-sql-server

# SQL Server harness (longer benchmark mode)
just perf-sql-server --benchmark
```

Equivalent `dotnet` commands:

```bash
dotnet run --project src/src/Benchmarks/Benchmarks.csproj -c Release -- source-generator
dotnet run --project src/src/Benchmarks/Benchmarks.csproj -c Release -- source-generator --benchmark
dotnet run --project src/src/Benchmarks/Benchmarks.csproj -c Release -- sql-server
dotnet run --project src/src/Benchmarks/Benchmarks.csproj -c Release -- sql-server --benchmark
dotnet run --project src/src/Benchmarks/Benchmarks.csproj -c Release -- all
```

Always use `-c Release`; running benchmarks in Debug produces meaningless numbers.

## Results

- Source-generator results are written to `artifacts/source-generator-performance/` (`history/` plus
  `latest.json`).
- SQL Server results are written to `artifacts/sqlserver-performance/` (`history/` plus `latest.json`).

Both harnesses exit non-zero when a regression threshold is not met.

## Adding a new benchmark scenario

- **Source generator:** add a scenario to `SourceGeneratorPerformanceScenarios.All` (name, generator
  factory, and source). The runner measures baseline, cold generation, warm rerun, and single-aggregate
  edit automatically.
- **SQL Server:** add a scenario method in `SqlServerStorePerformanceRunner` that measures an operation
  with `MeasureAsync(...)` and include the result in the returned `Scenarios` list.

See `docs/wiki/Source-Generator-Performance.md` for the source-generator thresholds and how to
interpret the history comparisons.