# Source Generator Performance

The source-generator performance harness measures how fast the aggregate and value-object
generators are and, more importantly, **how well their incremental pipeline caches work**.

## Running

```text
just perf-source-generator              # quick run (1 warmup, 3 measurement iterations)
just perf-source-generator --benchmark  # benchmark run (3 warmup, 12 measurement iterations)
```

Equivalent: `dotnet run --project src/src/Benchmarks/Benchmarks.csproj --configuration Release -- source-generator`.

The SQL Server harness (`just perf-sql-server` / `-- sql-server`) lives in the same `Benchmarks`
console project and measures event-store and snapshot timings against a SQL Server Testcontainer.

Each run writes a JSON snapshot to `artifacts/source-generator-performance/history/` and the latest
to `artifacts/source-generator-performance/latest.json`, then prints a summary compared against the
previous run. `artifacts/` is not committed.

## What is measured

For every scenario (`AggregateSimple`, `AggregateWithValueObjects`, `AggregateMulti`,
`ScalarValueObject`, `ComplexValueObject`) the harness records:

| Measurement | Meaning |
| --- | --- |
| `baseline` | Compile the source without any generator (framework cost floor) |
| `generator` | Cold generation on a fresh driver and compilation |
| `warm-rerun` | Incremental rerun of the same driver + compilation (cache hit) |
| `single-edit` | Rerun after exactly one aggregate changed in a five-aggregate compilation |

Ratios are printed against cold generation:

- `warm-rerun (X% of cold)` — an efficient incremental pipeline keeps this a small fraction.
- `single-edit (X% of cold)` — editing one aggregate should only regenerate that aggregate.

## Regression thresholds

The harness fails the run when a material regression is detected:

- `warm-rerun` must stay at or below **80%** of cold generation.
- `single-edit` must stay at or below **90%** of cold generation.

The thresholds are deliberately generous. They exist to catch pipeline changes that silently
regenerate everything on every build (for example dropping incremental caching, adding a global
non-incremental transform, or invalidating every aggregate on any edit) while remaining stable on
fast or shared machines.

## Interpreting history

The summary compares each scenario against `latest.json` from the previous run. When reporting a
regression, record the machine (`Machine`), framework (`Framework`), mode, and the history file so
the comparison conditions are reproducible. Compare runs on the same machine and mode.

## Comparison conditions

- All measurements run in-process on the machine where the harness is executed.
- The quick mode is for local iteration; the benchmark mode is for recorded comparisons and CI-style
  validation.
- Correctness is enforced separately by `SourceGenerator.UnitTests` (step-reason caching tests and
  byte-identical determinism tests); the performance harness is not a correctness substitute.