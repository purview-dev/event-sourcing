# Source Generator Performance

The source-generator performance harness measures how fast the aggregate and value-object generators
are and how their incremental pipeline behaves. It runs under BenchmarkDotNet (in-process toolchain)
in the same `Benchmarks` console project as the runtime and SQL Server suites.

## Running

```text
just perf-source-generator              # quick run (1 warmup, 3 iterations)
just perf-source-generator --benchmark  # benchmark run (3 warmup, 12 iterations)
```

Equivalent: `dotnet run --project src/src/Benchmarks/Benchmarks.csproj --configuration Release -- source-generator`.

Always use Release; in Debug the JIT produces meaningless numbers.

Each run writes a JSON snapshot to `artifacts/source-generator-performance/history/` and the latest
to `artifacts/source-generator-performance/latest.json`, then prints a summary compared against the
previous run. `artifacts/` is not committed.

## What is measured

For every scenario (`AggregateSimple`, `AggregateWithValueObjects`, `AggregateMulti`,
`ScalarValueObject`, `ComplexValueObject`) the harness records:

| Measurement | Meaning |
| --- | --- |
| `ColdGeneration` | Fresh compilation and driver, generate once (framework cost floor) |
| `WarmRerun` | Incremental rerun of the same driver + compilation |
| `SingleAggregateEdit` | Rerun after one aggregate changed in the five-aggregate `AggregateMulti` compilation |

Ratios are computed against cold generation and enforced as regression guards, and every case is
also compared against the previous run (40% mean regression threshold).

## Known incremental-caching hotspot

## Incremental caching

The generator emits an inert **pre-compilation marker** (`RegisterPreCompilationSourceOutput`,
experimental `RSEXPERIMENTAL007`) so Roslyn's `CompilationCache` reuses the previous run's compilation
reference on an identical rerun. This short-circuits the per-candidate `ForAttributeWithMetadataName`
transforms: a **warm rerun measures ~6–12% of cold generation** (the aggregate/value-object targets
report exactly `Cached`), so identical incremental builds are effectively free.

The harness also captures per-run **step run-reasons** (`Cached`/`Unchanged`/`Modified`/`New` per
pipeline stage) to `artifacts/source-generator-performance/steps.txt` and prints them, so a regression
that silently regenerates work on warm reruns is visible before the threshold trips.

The ratio thresholds are regression guards: warm-rerun must stay at or below **40%** of cold
generation (guarding the marker against silently regressing), and single-aggregate-edit at or below
**150%** (an edit inherently re-executes the changed aggregate's transform).

## Interpreting history

The summary compares each scenario against `latest.json` from the previous run. When reporting a
regression, record the machine (`Machine`), framework (`Framework`), mode, and the history file so
the comparison conditions are reproducible. Compare runs on the same machine and mode.

## Comparison conditions

- All measurements run in-process on the machine where the harness is executed.
- The quick mode is for local iteration; the benchmark mode is for recorded comparisons.
- Correctness is enforced separately by `SourceGenerator.UnitTests` (step-reason caching tests and
  byte-identical determinism tests); the performance harness is not a correctness substitute.