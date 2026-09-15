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

The incremental pipeline currently **re-executes most aggregate-generation steps on an identical
rerun**: a warm rerun measures close to cold generation for the aggregate scenarios, and a
single-aggregate edit also re-executes most work. Step-reasoning caching tests in
`SourceGenerator.UnitTests` prove the pipeline produces *stable* outputs (`Unchanged`), but the
framework's incremental stages do not fully short-circuit to `Cached` on identical inputs. This is a
known optimization target: making the intermediate transform results value-equal end-to-end would let
Roslyn skip re-execution and drive the warm-rerun ratio toward a small fraction of cold generation.

Because of this, the ratio thresholds are regression guards (warm-rerun and single-edit must stay at
or below **150%** of cold generation) rather than aspirational targets: they catch pipeline changes
that make reruns slower than cold generation or grossly break caching, without failing on the current
known hotspot. Track the warm/cold ratio across runs; if it starts trending down after an
incremental-caching improvement, tighten the thresholds accordingly.

### Investigated: pre-compilation source output

A spike registered `RegisterPreCompilationSourceOutput` (experimental, `RSEXPERIMENTAL007`) to emit an
inert marker file so Roslyn's `CompilationCache` would reuse the previous run's compilation reference.
The step-reason tests confirmed the aggregate targets became exactly `Cached` (the per-candidate
transform was no longer re-executed). However, the measured warm-rerun time did **not** improve: the
run is dominated by driver-level overhead that persists even when every step is cached, so the warm
rerun stayed ≈ cold generation while cold generation picked up the extra marker file. The spike was
therefore **reverted**: the re-execution is upstream of the generator (Roslyn's driver regenerates the
compilation from post-initialization attribute trees and compares it by reference), and the
`ForAttributeWithMetadataName` re-execution is not the measured bottleneck.

## Interpreting history

The summary compares each scenario against `latest.json` from the previous run. When reporting a
regression, record the machine (`Machine`), framework (`Framework`), mode, and the history file so
the comparison conditions are reproducible. Compare runs on the same machine and mode.

## Comparison conditions

- All measurements run in-process on the machine where the harness is executed.
- The quick mode is for local iteration; the benchmark mode is for recorded comparisons.
- Correctness is enforced separately by `SourceGenerator.UnitTests` (step-reason caching tests and
  byte-identical determinism tests); the performance harness is not a correctness substitute.