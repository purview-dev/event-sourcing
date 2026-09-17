# Agent Instructions

## Purpose and authority

This repository contains the `Purview.EventSourcing` framework, its incremental source generator, storage providers, administration packages, sample applications, documentation, and TUnit test suites.

- This file is the repository-wide source of truth for AI agents. More-specific `AGENTS.md` files, if added later, take precedence for their subtrees.
- `.github/copilot-instructions.md` bootstraps GitHub Copilot into this file; keep it aligned if instruction locations change.
- Follow explicit user instructions first, then the nearest applicable repository instructions, then established code patterns.
- Operate only in this repository unless the user explicitly expands the scope. `P:\GitHub\purview-dev\sourcegenerator-framework` may be inspected as read-only reference material, but do not edit it or assume its conventions override this repository.
- Never read, copy, log, or commit secrets from excluded files, environment variables, user profiles, local configuration, test output, or provider credentials.

## Repository boundaries and working tree safety

- Preserve all pre-existing modified, deleted, and untracked files. They belong to the user unless the task explicitly identifies them as agent-owned.
- Before editing, inspect `git status --short` and relevant diffs. Work around unrelated changes and do not revert, overwrite, format, stage, or commit them.
- Keep changes focused and reviewable. Avoid drive-by cleanup, broad renames, dependency churn, or architectural refactors unrelated to the requested outcome.
- Do not edit generated output, `artifacts/`, `TestResults/`, build output, or caches as source. Fix the generator, template, configuration, or input that owns the output.
- Do not use destructive Git commands such as `git reset --hard` or `git checkout --` to clean the worktree.
- Before completion, inspect the final diff and report validation that was run, skipped, or blocked by missing infrastructure.

## Source of truth and layout

| Path | Purpose |
| --- | --- |
| `src/EventSourcing.slnx` | Canonical solution for restore, build, test, and pack |
| `src/src/EventSourcing` | Core abstractions, aggregate runtime, public contracts, and SDK/build assets |
| `src/src/SourceGenerator` | Roslyn incremental generator and analyzer metadata |
| `src/src/{AzureStorage,CosmosDb,InMemory,MongoDB,Postgres,SqlServer}` | Storage-provider implementations |
| `src/src/ImplementationShared` | Shared provider implementation support; not a public package |
| `src/src/Validation.FluentValidation`, `src/src/Validation.ZodSharp` | Optional validation integrations |
| `src/src/Admin.*` | Administration abstractions, security, API, UI, and provider adapters |
| `src/src/Samples*` | Reference domains, quick start, web app, shared defaults, and Aspire AppHost |
| `src/tests` | Unit, integration, source-generator, sample, admin, and performance projects |
| `src/tests/SharedTestingFramework` | Shared provider fixtures and Testcontainers infrastructure |
| `docs/wiki` | User-facing design, provider, generator, dependency, and release guidance |
| `.github/workflows` | PR validation, reusable validation/pack, and release workflows |
| `.agents/skills` | Repository workflow and SDK guidance available to supporting agents |
| `Directory.Packages.props` | Centrally managed NuGet versions |
| `src/Directory.Build.props` / `src/Directory.Build.targets` | Solution-wide SDK, package, analyzer, and build behavior |
| `global.json` | Required .NET SDK and Microsoft.Testing.Platform selection |
| `package.json` | Authoritative repository/package version and Changesets package identity |
| `Justfile` | Supported local workflow commands |

Do not introduce a second version source, dependency-version location, or parallel build entrypoint without an explicit repository-wide migration.

## Standard workflow

1. Read this file, inspect the working tree, and locate the implementation, tests, documentation, and existing patterns relevant to the task.
2. Confirm behavior from code and tests rather than relying on memory or documentation alone. Documentation describes intent but may lag implementation.
3. Make the smallest coherent change. Preserve public behavior unless the task explicitly changes it.
4. Update tests for fixes and behavior changes. Update documentation when public behavior, provider support, configuration, query semantics, generated API shape, or limitations change.
5. Run the narrowest meaningful validation first, then broader validation in proportion to risk.
6. Review the diff for unrelated edits, generated noise, compatibility risks, and missing docs or tests.

Use repository-local skills when their trigger applies:

- `dotnet-tunit` for writing TUnit tests and `tunit-test-runner` for executing or filtering them.
- `project-placement-defaults`, `sdk-configuration-reference`, and `sdk-project-behavior-and-detection` for project layout or `Purview.BuildSdk` behavior.
- `git-conventional-commits` for commit work and `lefthook-integration` for Git-hook changes.

## Architecture and domain invariants

- Event streams are the canonical source of aggregate truth. Snapshots are replaceable optimizations/read models and must never become the only source of business state.
- Preserve aggregate identity, stream ordering, optimistic-concurrency expectations, event metadata, schema version, and replay semantics across all storage providers.
- Aggregate state changes should flow through events. Do not add service-layer mutations that bypass event creation and replay.
- Keep invariants close to aggregate hooks or value objects rather than scattering them across application services or provider code.
- Treat emitted event types, serialized payloads, event names, schema versions, generated method signatures, and persistence formats as compatibility-sensitive contracts.
- Avoid provider-specific behavior in core abstractions. Shared capabilities belong in the core contract only when every applicable provider can implement them consistently or limitations are explicitly modeled and documented.
- Preserve cancellation, asynchronous I/O, exception meaning, ordering, atomicity, and transaction boundaries when extending persistence APIs.

## Aggregate and event conventions

- Aggregates intended for generation remain `partial` and use `[GenerateAggregate]` plus generated event methods where applicable.
- Follow the established aggregate, event, and generated-method naming rules documented in `docs/wiki/Source-Generator-Behaviors.md` and demonstrated in `src/src/Samples`.
- Prefer `EventStoreList<T>` and `EventStoreSet<T>` for collection state that participates in generated events. Do not substitute ordinary mutable collections unless their different replay semantics are intentional and tested.
- Use `[Computed]` for derived event values callers must not provide. Compute such values deterministically from canonical state and method inputs.
- Keep generated-method hooks deterministic and replay-safe. Validate inputs before committing an event; do not perform external I/O inside state-application paths.
- Preserve event metadata such as `EventDetails` when transforming or upcasting events.
- Additive payload changes should remain backward compatible. Breaking payload changes require an explicit schema-version/upcaster strategy; semantic changes generally require a new event type. Follow `docs/wiki/Event-Versioning-Strategy.md`.
- Test event replay from historical shapes, unknown-event behavior, and multi-hop upcasting when schema evolution changes.

## Value objects, validation, and serialization

- Prefer existing value-object generation and conversion patterns over handwritten duplication.
- Keep value objects immutable, validation deterministic, and equality/hash behavior aligned with every value that defines identity.
- Changes to generated value-object conversions require generator tests and runtime/provider coverage where serialization or query translation is affected.
- Validation adapters must remain optional. Core APIs must not acquire mandatory FluentValidation or Purview.ZodSharp dependencies.
- Preserve the documented Purview.ZodSharp direct-reference guardrail and its build-time enforcement; update `docs/wiki/Dependency-Guardrails.md` if it changes.
- Use the repository's configured serializers and provider converters. Do not silently change property names, casing, enum representation, null handling, or stored JSON shape.
- When introducing polymorphic or versioned serialization, cover old payload deserialization as well as new payload round trips.

## Source generator rules

- The generator targets `netstandard2.0`; do not use APIs unavailable to that target.
- Follow Roslyn incremental-generator practices: derive output from declared inputs, keep transforms deterministic, avoid mutable global state and filesystem/environment dependencies, and make cancellation effective.
- Prefer the existing Purview SourceGeneratorFramework APIs and local generator helpers. The separate Source Generation Framework repository is reference-only.
- Generated output must be stable for identical input. Avoid nondeterministic ordering, timestamps, machine-specific paths, or culture-sensitive formatting.
- Diagnostics are public developer experience: preserve IDs and meanings, choose accurate locations and severity, and update `AnalyzerReleases.Unshipped.md` for newly introduced or changed diagnostics as required by analyzer conventions.
- Test positive generation, diagnostics, invalid/partial input, namespaces and nesting, generics/nullability, inheritance, accessibility, and deterministic output as relevant.
- Source-generator changes normally require tests in `src/tests/SourceGenerator.UnitTests`; performance-sensitive pipeline changes may also require the performance harness.

## Provider implementation rules

- Keep event-store behavior consistent across Azure Storage, Cosmos DB, InMemory, MongoDB, Postgres, and SQL Server where the provider advertises the same capability.
- When changing a shared store contract, audit every implementation, options type, dependency-injection registration, admin adapter, shared fixture, and provider guide.
- Provider options should use established options/binding/validation patterns, predictable defaults, and actionable validation errors. Do not embed credentials or environment-specific endpoints.
- Propagate `CancellationToken` through provider calls. Avoid synchronous blocking over asynchronous APIs.
- Preserve optimistic concurrency, idempotency expectations, event order, range/time boundary semantics, transaction behavior, and rollback behavior.
- Keep SQL identifiers, Mongo collections, Cosmos containers/partitions, and Azure Table/Blob routing configurable through established abstractions. Validate identifiers rather than concatenating untrusted input into commands.
- Use provider-native parameterization and escaping. Never construct SQL or provider queries by interpolating untrusted values.
- Integration tests should exercise the actual provider when behavior depends on translation, serialization, concurrency, indexing, transactions, or SDK behavior; an in-memory substitute is insufficient.

## Snapshots and query translation

- Snapshots must be reconstructible from event streams. Snapshot write/read failures must not redefine aggregate truth.
- SQL snapshot queries can translate deep predicates over directly mapped complex JSON graphs.
- Provider-converted scalar value objects may not support deep member translation through `.Value`.
- If deep SQL predicates are required for a complex scalar concept, prefer a separately mapped complex mirror property derived from canonical state and cover it with provider integration tests.
- Keep mirror/read-model properties synchronized from canonical event-applied state; callers must not mutate them independently.
- Query changes must define and test inclusivity, ordering, empty results, unknown streams, time zones, version bounds, paging, and cancellation as applicable.
- Document newly supported query shapes or provider limitations in `docs/wiki/Provider-Feature-Matrix.md` and the affected provider guide.

## Public API, dependencies, and project configuration

- Treat public and protected types/members, DI registration methods, option names/defaults, attributes, diagnostics, generated code, and NuGet package contents as public API.
- Prefer backward-compatible additions. Do not rename, remove, narrow accessibility, or alter semantics without explicit approval and corresponding migration/release documentation.
- Centralize package versions in `Directory.Packages.props`; project files should normally contain versionless `PackageReference` entries.
- Reuse existing dependencies when practical. New dependencies require a clear need, compatible target frameworks, acceptable licensing/security posture, and correct public/private asset flow.
- Respect `Purview.BuildSdk` inference before adding manual properties or references. Repository-wide bootstrap properties that affect `Sdk.props` belong before the SDK import in `src/Directory.Build.props`.
- Place source projects under `src/src` and test projects under `src/tests`, beside the closest peer. Follow established suffixes such as `.UnitTests`, `.IntegrationTests`, and `.PerformanceTests`. Performance harnesses are console projects consolidated under `src/src/Benchmarks` (`Benchmarks.csproj`), not test projects.
- Keep packability explicit and package metadata consistent. Verify package contents when changing build assets, analyzers, transitive targets, README files, or project-reference packing.

## Administration, samples, and Aspire

- Keep admin abstractions provider-neutral; provider-specific persistence belongs in the corresponding `Admin.*` adapter.
- Preserve authorization boundaries in `Admin.Security`, `Admin.API`, and `Admin.Site`. Do not expose event payloads, mutation endpoints, or operational data without the established authorization policy.
- Samples are executable documentation. Keep them idiomatic, comprehensible, and aligned with supported public APIs rather than using internal shortcuts.
- Update sample tests and relevant README/wiki examples when a public workflow changes.
- `Samples.AppHost` defines external infrastructure for local distributed execution. Prefer its established resources and configuration wiring; do not hardcode local ports, credentials, or machine-specific paths.
- Aspire/Testcontainers work may require Docker and provider images. Report infrastructure prerequisites or failures rather than weakening tests to avoid them.

## Testing conventions

- This repository uses TUnit on Microsoft.Testing.Platform, selected in `global.json`. Do not add xUnit, NUnit, MSTest, or FluentAssertions patterns unless explicitly requested.
- TUnit test methods use `[Test]`, and assertion calls are awaited, for example `await Assert.That(actual).IsEqualTo(expected);`.
- Never use VSTest `dotnet test --filter` for these projects. Use `--treenode-filter` and observe the command form supported by the repository/SDK.
- Prefer the smallest relevant project or tree-node filter during iteration. A typical solution-wide command is:

  ```text
  dotnet test --project src/EventSourcing.slnx --configuration Debug --treenode-filter "/*/*/*/*" --ignore-exit-code 8
  ```

- PR validation restores and builds the Release solution, then runs the unit-test tree filter `/*/*/*/*[Category=Unit]` (the `Purview.BuildSdk` applies `[assembly: TUnit.Core.Category("Unit")]` to unit-test projects) and emits TRX results.
- Unit tests should cover domain logic, contracts, failure behavior, and regressions without external infrastructure.
- Source-generator tests should assert generated code and diagnostics using the existing testing framework.
- Provider integration tests use `src/tests/SharedTestingFramework` and Testcontainers or provider infrastructure. Run affected-provider tests for translation, persistence, concurrency, or serialization changes when infrastructure is available.
- Multi-provider contract changes require shared tests plus affected provider suites; do not prove parity with only one provider.
- Performance work should use the source-generator or SQL Server performance harness and report comparable before/after conditions; performance tests are not a substitute for correctness tests.
- Documentation-only changes do not require the .NET suite unless they alter executable snippets, build/release commands, or configuration whose validity needs testing.
- Do not update approved/verified output merely to make a failure disappear. Inspect the semantic difference first.

## Documentation rules

- Keep `README.md`, package/provider READMEs, and `docs/wiki` aligned with actual supported behavior.
- Update `docs/wiki/Home.md` when navigation or major capabilities change.
- Update `docs/wiki/Provider-Feature-Matrix.md` and provider-specific guides when capabilities, limitations, configuration, query translation, or transaction support changes.
- Update `docs/wiki/Source-Generator-Behaviors.md` for generated shapes, naming, hook semantics, validation, conversion, or diagnostics changes.
- Update event-versioning guidance when replay, schema-version, upcaster, or unknown-event behavior changes.
- Examples must compile conceptually against current public APIs. Use placeholders for credentials and environment-specific values.
- Explain provider-specific limitations directly; do not imply parity that tests and implementation do not provide.

## Build, format, test, and pack commands

Prefer the `Justfile` recipes or their equivalent commands:

```text
dotnet tool restore
just pipeline-pr                 # full PR pipeline (restore, build, lint, unit tests, pack, validate)
just pipeline-build              # restore, build, lint, pack, validate (no tests)
just restore
just build
just test                        # accepts a treenode filter and extra arguments
just lint-check
just pack
just perf-source-generator
just perf-sql-server
just current-version
```

The repository uses the shared `Purview.Build` pipeline (`purview-build.json` at the root), also driven in CI through the `purview-dev/build` reusable workflows. `just pipeline-pr` installs the pinned `Purview.Build` tool to `.tools/purview-build` when missing.

Local CI-equivalent validation:

```text
dotnet restore src/EventSourcing.slnx
dotnet build src/EventSourcing.slnx --no-restore --configuration Release
dotnet test src/EventSourcing.slnx --no-build --configuration Release --ignore-exit-code 8 -- --treenode-filter "/*/*/*/*[Category=Unit]"
dotnet csharpier check .
```

- Use `dotnet csharpier check .` for validation (the pipeline lints the repository root). Run the rewriting formatter only when formatting changes are in scope, and ensure it does not touch unrelated user files.
- Local `just` recipes default to the `Debug` configuration; Release-style builds, tests, and packs run through the `Purview.Build` pipeline (`just pipeline-pr` / `just pipeline-build`).
- The pipeline discovers tests under `src/tests` with `Build:TestPatterns` (`*Tests.csproj`), restricts the run list with `Build:TestProjects` (`*UnitTests.csproj`), and applies `Build:TestFilter` (`/*/*/*/*[Category=Unit]`), so GH Actions never runs provider integration tests or the performance harnesses; run those locally with `just test` when Docker/Testcontainers infrastructure is available.
- Pack when package assets, public package dependencies, analyzers, build targets, or packaging metadata change.
- Build the canonical solution when shared contracts, project configuration, central packages, or generator/package wiring change.

## Versioning, changesets, and releases

- `package.json` is the authoritative release/package version. `UsePackageJsonVersion` is strict; do not manually diverge project versions.
- User-facing package changes normally require a Changeset when release preparation is in scope. Changeset text must describe actual consumer-visible behavior.
- Version application updates `package.json`, `CHANGELOG.md`, and consumes the relevant `.changeset` files. Do not hand-edit only one part of that result.
- Release is automatic on push to `main`: the `Release` workflow runs the shared `Purview.Build` pipeline with `Release:Mode=NuGet`, publishing packages and creating the `v<version>` GitHub release only when that tag does not already exist. NuGet publishing uses the `NUGET__APIKEY` organization secret.
- Never create release tags or publish packages manually unless the user explicitly requests a documented recovery procedure. See `docs/wiki/Release-Flow.md`.

## Completion checklist

Before handing work back:

- Confirm the requested behavior and scope are satisfied.
- Confirm only intended files changed and all pre-existing worktree changes remain intact.
- Review public API, serialization, event-schema, provider-parity, security, and package-content implications.
- Add or update focused tests for code changes and verify failures are not hidden by empty filters or ignored infrastructure errors.
- Update all affected docs, samples, feature matrices, analyzer metadata, and release notes when applicable.
- Run appropriate build, test, formatting, and pack checks in proportion to risk.
- State exactly what validation ran. If a check was skipped or blocked, give the concrete reason and remaining risk.
