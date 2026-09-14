# Release flow

This repository uses the shared [Purview.Build](https://github.com/purview-dev/build) pipeline for both PR validation and releases. Consuming repositories own configuration (through `purview-build.json`) but not pipeline source code.

- `.github/workflows/pr.yml` — PR validation
- `.github/workflows/release.yml` — release on push to `main`
- `purview-build.json` — pipeline configuration

## PR validation

`pr.yml` runs on pull requests targeting `main` and delegates to the shared `purview-build.yml` workflow. It runs:

1. `dotnet restore` of `src/EventSourcing.slnx`
2. `dotnet build --no-restore --configuration Release`
3. CSharpier lint across the repository
4. Unit tests (discovered under `src/tests` matching `*Tests.csproj`, run with the `/*/*/*/*[Category=Unit]` TUnit tree-node filter)
5. `dotnet pack` and package-content validation

Integration tests are never discovered in CI: `purview-build.json` sets `Build:TestPatterns` to `*Tests.csproj`, `Build:TestProjects` to `*UnitTests.csproj`, and `Build:TestFilter` to `/*/*/*/*[Category=Unit]`, so only unit-test projects (tagged `[Category=Unit]` by the `Purview.DotNetProjectSdk`) are executed; provider integration tests (which require Docker/Testcontainers) run only locally via `just test`. The performance harnesses live under `src/src/Benchmarks` (a single non-test `Benchmarks.csproj`) and run locally via `just perf-source-generator` / `just perf-sql-server`.

The PR workflow does not tag, release, or publish packages.

## Versioning model

`package.json` is the authoritative release version source. The release workflow reads:

```bash
bun -p "require('./package.json').version"
```

This flow assumes version prep already happened before release (for example with `@changesets/cli` versioning and changelog updates merged to `main`). The release pipeline does not invent or auto-bump versions.

## Release on push to main

`release.yml` triggers on push to `main` and delegates to the shared `purview-release.yml` workflow with `release-mode: NuGet`.

The shared workflow:

1. Reads `package.json` `version` and computes the `v<version>` tag.
2. Skips the entire release if `v<version>` already exists (so re-merging to `main`, or merging `main` into a `release` branch, releases exactly once).
3. Restores, builds, lints, runs unit tests, packs, and validates packages.
4. Pushes every `.nupkg` to nuget.org (`--skip-duplicate`).
5. Creates the `v<version>` GitHub release with generated release notes and attaches the package artifacts.

A release is therefore produced simply by bumping `package.json` (via changesets) and merging to `main`. Do not create release tags manually.

## Prerelease support

Prerelease versions (any SemVer containing a hyphen, for example `2.0.0-prerelease.29`) release through the same push-to-`main` flow. The `v<version>` tag and GitHub release are still created and packages published; the shared pipeline does not mark the GitHub release with the prerelease flag.

## NuGet publishing

NuGet publishing uses the shared workflow's API-key path with the organization `NUGET__APIKEY` secret (available through `secrets: inherit`). The pipeline also accepts `NUGET_APIKEY`. No long-lived repository-level API key secrets are required.

To use NuGet Trusted Publishing (OIDC) instead, the consuming repository would need to mint the federated credential before the shared pipeline runs; the shared workflow itself does not perform the `NuGet/login` step.

## Shared pipeline configuration

`purview-build.json` at the repository root drives the pipeline:

| Key | Value | Purpose |
| --- | --- | --- |
| `Build:Solution` | `src/EventSourcing.slnx` | Solution passed to restore/build/pack |
| `Build:TestRoot` | `src/tests` | Test project discovery root |
| `Build:TestPatterns` | `*Tests.csproj` | Test projects discovered for the test step |
| `Build:TestProjects` | `*UnitTests.csproj` | Restricts the run list to unit-test projects (excludes integration tests and the `src/src/Benchmarks` harnesses) |
| `Build:TestFilter` | `/*/*/*/*[Category=Unit]` | TUnit tree-node filter (unit-only) |
| `PackValidation:RequireSymbolPackage` | `true` | Every `.nupkg` needs a matching `.snupkg` |
| `PackValidation:RequireSymbolFiles` | `true` | Every `.snupkg` must contain PDBs |
| `PackValidation:RequiredContent` | Expected package contents | Asserts each package ships its expected output — README/logo, `buildTransitive/Purview.EventSourcing.targets` in the core package, `buildTransitive/Purview.EventSourcing.Validation.ZodSharp.targets` in the ZodSharp package, the analyzer assemblies in the core/EF-Core-enabled packages, and the provider/admin `lib` assemblies |
| `Release:Mode` | `None` | Publishing is enabled only by the release workflow |

Configuration precedence is command line, environment variables, `purview-build.json`, then the tool's built-in defaults. Nested environment keys use `__`, for example `Release__Mode=NuGet`.
