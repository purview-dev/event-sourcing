---
name: purview-architests
description: Adopt or debug Purview.Architecture.Testing, the package that enforces a repository's project and type-level dependency policy as TUnit tests. Use when adding architecture tests to a repository, writing or changing .config/architecture-policy.json, validating a policy with the architests tool, enabling the assembly, type, API or port rules, recording or expiring a waiver, setting a dependency budget, interpreting a policy-integrity, dependency-matrix, type-level, cycle, sibling-independence, composition-boundary, forbidden-package, framework-reference or naming failure, or when the architecture tests pass but enforce nothing.
license: MIT
metadata:
  product: Purview.Architecture.Testing
  repository: purview-dev/architests
---

# Architecture tests with Purview.Architecture.Testing

The package defines the rules once so a policy is not copied into every repository's test project.

## Adoption is two things

**1. A policy file** at `.config/architecture-policy.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/purview-dev/architests/main/architecture-policy.schema.json",
  "version": 3,
  "sourceRoots": ["src"],
  "responsibilities": [
    {
      "name": "Core",
      "aliases": ["Domain"],
      "category": "domain",
      "purpose": "The domain model, its invariants and its rules.",
      "mayDependOn": [],
      "mustNotDependOn": ["Application", "Persistence"],
      "forbiddenFrameworkReferences": ["Microsoft.AspNetCore.App"]
    }
  ]
}
```

**2. One derived test class:**

```csharp
[InheritsTests]
public sealed class ArchitectureTests : PurviewArchitectureTests;
```

## `[InheritsTests]` is load-bearing

TUnit does **not** walk base types during discovery. Without the attribute the class compiles, the
run is green, and nothing is enforced — the worst failure this package can have.

TUnit's analyzer reports `TUnit0030` when it is missing. Treat it as an error, not a suggestion.

If the architecture tests "pass" but you cannot name which rule ran, check this first.

## The rules

| Rule id | Test | What it catches |
| -- | -- | -- |
| `policyIntegrity` | `Policy_GivenItsOwnDefinitions_IsConsistent` | An unknown responsibility name, a dependency both permitted and forbidden, or a duplicate name or alias. Not waivable |
| `graphDiscovery` | `Repository_GivenItsSourceRoots_YieldsProjectsToCheck` | An empty graph — which satisfies every other rule |
| `naming` | `ProductionProjects_GivenTheirNames_MapToAResponsibility` | A project name matching no responsibility or alias |
| `discouragedName` | `ProductionProjects_GivenTheirNames_AvoidCatchAllNames` | A project named after a category, so nothing can be kept out of it |
| `dependencyMatrix` | `ProductionProjects_GivenTheirReferences_RespectTheDependencyMatrix` | A declared `ProjectReference` the matrix forbids or does not permit |
| `assemblyDependencyMatrix` | `AssemblyTypes_GivenTheirReferences_RespectTheDependencyMatrix` | A **type** in one responsibility using a type in another, including a dependency that only arrives transitively or through a package |
| `typeNaming` | `TypeRules_GivenTheRepository_RespectThePolicy` | A public interface without `I`, an attribute or exception without its suffix, or a discouraged suffix |
| `typeShape` | `TypeRules_GivenTheRepository_RespectThePolicy` | An unsealed public class, a public field, a mutable collection return, or a non-static utility class |
| `typeNamespace` | `TypeRules_GivenTheRepository_RespectThePolicy` | A public type outside the project's root namespace |
| `attributePolicy` | `TypeRules_GivenTheRepository_RespectThePolicy` | A missing or forbidden attribute, `[Obsolete]` without a message, or `InternalsVisibleTo` naming production code |
| `apiSurface` | `APIRules_GivenTheRepository_RespectThePolicy` | The public API differs from the committed baseline |
| `apiDocumentation` | `APIRules_GivenTheRepository_RespectThePolicy` | A public type or member has no XML documentation |
| `portImplementation` | `PortRules_GivenTheRepository_RespectThePolicy` | A port in an inner layer has no implementation |
| `portPurity` | `PortRules_GivenTheRepository_RespectThePolicy` | A port exposes an infrastructure technology |
| `dependencyBudget` | `AdoptionRules_GivenTheRepository_RespectThePolicy` | A project references beyond its budget, or the budget still allows a reference that has gone |
| `siblingIndependence` | `SiblingImplementations_GivenTheirReferences_DoNotDependOnEachOther` | A qualified implementation referencing a qualified sibling |
| `compositionBoundary` | `Implementations_GivenTheirReferences_DoNotDependOnComposition` | A non-host project referencing the composition root (`category == "composition"`) |
| `forbiddenPackage` | `ProductionProjects_GivenTheirPackages_AvoidForbiddenFrameworkDependencies` | A package matching a forbidden prefix for that responsibility |
| `forbiddenFramework` | `ProductionProjects_GivenTheirFrameworkReferences_AvoidForbiddenFrameworks` | A `<FrameworkReference>` matching a forbidden prefix, e.g. `Microsoft.AspNetCore.App` in a portable layer |
| `cycle` | `ProjectGraph_GivenItsReferences_HasNoCycles` | A dependency cycle, reported readably |
| `testReference` | `ProductionProjects_GivenTheirReferences_DoNotReferenceATestProject` | Production code referencing a test project |
| `waiverIntegrity` | `PolicyWaivers_GivenTheirDefinitions_AreJustifiedAndStillApply` | A waiver that is stale, unjustified, malformed, or excuses an unknown rule id |

A separate test validates the policy against the schema first, because a policy that does not match
its schema may be silently missing a rule.

## Policy integrity

`policyIntegrity` always runs and is **not waivable**, so a broken policy cannot be excused. It
reports what a JSON Schema cannot express:

- a `mayDependOn` or `mustNotDependOn` entry that names no responsibility — aliases and project
  names do not count in the matrix;
- the same responsibility in both lists, which is a contradiction rather than a choice;
- two responsibilities with the same name, or an alias that equals a name or another alias;
- with `checkReachability`, a `mustNotDependOn` the permitted `mayDependOn` closure can reach. The
  message prints the path, such as `Core -> Application -> Persistence`.

```json
"policyIntegrity": {
  "checkReachability": true
}
```

Reachability is opt-in because it is a stronger claim: the direct lists can be consistent while a
longer permitted path still defeats the prohibition.

## Check it without a test run

```bash
dotnet tool install --global Purview.Architecture.Testing.Cli
architests validate
architests waivers list --stale
```

`validate` runs the schema check and `policyIntegrity`; it needs no build. Exit codes are `0` clean,
`1` findings, `2` a malformed command line. Both commands accept `--repository <path>`; without it
the tool walks up from the current directory and stops at the repository boundary.

`graphDiscovery` runs before the rules it protects. If `sourceRoots` is wrong, discovery finds
nothing and every other rule passes vacuously — that is what this rule exists to stop.

## The type-level rule

A project file only shows a declared `ProjectReference`. `assemblyDependencyMatrix` reads the
compiled assemblies by **metadata only** — no assembly is loaded or executed — and checks every
external type each one references against the same `mayDependOn` / `mustNotDependOn` matrix. It
catches what a project file cannot: a type that only arrives transitively, or through a package.

```json
"assemblyRules": {
  "enabled": true,
  "assemblyRoots": [],
  "assemblyGlobs": ["*.dll"]
}
```

- **Off by default.** It needs a build to have produced the assemblies, so it is opt-in.
- An empty `assemblyRoots` scans the test output directory, which already holds every project the
  test project references, directly or transitively. Set it to repo-relative folders only if the
  assemblies are somewhere else.
- Semantics mirror `dependencyMatrix`: same-responsibility references are always allowed; a
  reference the matrix neither permits nor forbids is a violation.
- Enabled with nothing found is a **failure**, not a silent pass — the same guard `graphDiscovery`
  provides for projects.
- Qualified implementations collapse into their responsibility, so `Persistence.SqlServer` is
  `Persistence` at the type level too. Qualification is a naming choice, never a different rule.

Assemblies are attributed to projects by the SDK's generated `<ProjectName>Assembly` marker when a
project's `AssemblyName` is unrelated to its name, and by name otherwise.

## Type naming, shape and namespace

The same reader exposes the public declarations a project compiles, which powers four opt-in rules:
`typeNaming`, `typeShape`, `typeNamespace` and `attributePolicy`. They are configured under
`typeRules`, off by default, and share the assembly roots and globs above.

```json
"typeRules": {
  "enabled": true,
  "appliesTo": { "categories": ["domain", "application", "shared"] }
}
```

Use the `purview-architests-type-rules` skill for what each rule catches, why a type is reported, and
how to fix or waive it. A disabled group produces no test, so a missing type-rule test is a policy
setting rather than a silent pass.

## Classifying test and production projects

The package decides which projects the rules govern from the project graph:

- A project is a **test project** when its name ends with `Tests`, **or** its path contains a
  `tests` directory. Test projects are excluded from every rule — a test references everything by
  design, so applying the matrix to it would make the rule set unusable.
- Everything else is a **production project**, and every rule applies to it.

## API baseline and ports

Four more rules are opt-in. `apiRules` compares the public API with a committed baseline and checks
its documentation; `portRules` checks that inner-layer interfaces are implemented and stay
technology-neutral.

```json
"apiRules": { "enabled": true },
"portRules": { "enabled": true }
```

Write the baseline with the tool, never by hand:

```bash
architests api capture   # write .config/api-surface.json
architests api check     # exit 1 on any difference
```

A missing baseline is itself an `apiSurface` failure. A removal is reported as breaking; an addition
still needs a reviewed baseline change, because the baseline is the contract. The
`purview-architests-contracts` skill covers the baseline document, documentation requirements and
the port rules in detail.

## Dependency budget

`dependencyBudget` is a ratchet on the project graph: it records the references each project may
declare today, so a new dependency needs a reviewed policy change.

```json
"dependencyBudget": {
  "enabled": true,
  "failOnStale": true,
  "entries": [
    { "project": "Application", "references": ["Domain"] }
  ]
}
```

- A reference outside the entry is a `dependencyBudget` failure.
- With `failOnStale` (the default), an allowance the project no longer uses fails too, so the budget
  only tightens.
- It is **not waivable**, and a project without an entry is not budgeted.
- Start with `entries: []`, then add one project at a time.

## See it working

`src/samples/ArchitectureSample` is a six-project repository that exercises every rule group: a port
with two adapters, a waived legacy project, the type rules, the committed baseline and a dependency
budget. Use it as the reference when a policy question is easier to answer from a working example
than from prose.

## Prefer a waiver to a skip

```csharp
protected override bool SkipRule(string rule) => rule == ArchitectureRules.DiscouragedName;
```

`SkipRule` removes a rule for the whole repository, with no record. A waiver is better: it is per
project, carries a reason and a review date, keeps every other rule in force for that project, and
is itself checked by `waiverIntegrity`.

```json
"waivers": [
  {
    "project": "Legacy.Utilities",
    "mapsTo": "Shared",
    "rules": ["naming"],
    "reason": "Predates the catalogue; renaming breaks a published package id.",
    "reviewedOn": "2026-01-14"
  }
]
```

`mapsTo` is the useful part: it maps an established project onto the responsibility it actually has,
so the dependency rules still apply in full and only the *name* is excused.

Any waivable rule id may be waived, including `siblingIndependence` and `assemblyDependencyMatrix`.
An unknown id is a `waiverIntegrity` failure, so a typo cannot silently excuse nothing. Naming a
guard (`policyIntegrity`, `graphDiscovery` or `waiverIntegrity`) is also a failure: those rules exist
to keep the policy falsifiable, so a waiver that appears to excuse them would be a lie.

### Waiver expiry

```json
"waiverPolicy": {
  "maxAgeDays": 180
}
```

A waiver reviewed longer ago than the maximum is a `waiverIntegrity` failure. So are a waiver with
no `reviewedOn` while a maximum is set, a date that is not `yyyy-MM-dd`, and a date in the future.
Expiry is off by default; adopting it turns the review date from a note into an obligation.

## Adding your own rule

A repository can add a bespoke check without a second architecture framework. Implement
`IArchitectureRule` and return the same `ArchitectureViolation` type as the built-in rules:

```csharp
[InheritsTests]
public sealed class ArchitectureTests : PurviewArchitectureTests
{
    protected override IReadOnlyList<IArchitectureRule> AdditionalRules => [new NoConsoleLoggingRule()];

    sealed class NoConsoleLoggingRule : IArchitectureRule
    {
        public string Rule => "noConsoleLogging";

        public IReadOnlyList<ArchitectureViolation> Evaluate(ArchitectureContext context) =>
            // context.Policy, context.Projects and context.Assemblies
            [];
    }
}
```

Each additional rule becomes its own `[Test]` (`AdditionalRule_GivenTheRepository_RespectsThePolicy`),
so a failure names the rule. It participates in the same failure reporting, `SkipRule` (by its
`Rule` id) and waiver model — a waiver whose `rules` names it is honoured automatically.

The `purview-architests-extending` skill covers the full extension point and the package's internals.

## Reading a failure

Each rule is its own test, so a failure names the rule rather than collapsing the set into one
assertion. The `.Because(...)` text lists every violation the rule found.

- `naming` — a project name is `<Responsibility>[.<Implementation>]`, and only the **first segment**
  is classified. `Persistence.SqlServer` and `Persistence` are both Persistence. Add an alias, or a
  waiver with `mapsTo`.
- `dependencyMatrix` / `assemblyDependencyMatrix` — fix the reference, or move the type. Widening
  `mayDependOn` is the last resort, and it is a policy change that needs review.
- `forbiddenPackage` — by default the package is one the project **owns**: declared, injected by
  `Directory.Build.props`, or pulled in by its own package references. A package inherited from a
  permitted project reference is not reported, so a host is not blamed for its adapter’s EF Core.
  Set `forbiddenPackageScope: "closure"` on the responsibility only when the strict compile-graph
  guarantee is intended.
- `forbiddenFramework` — a `<FrameworkReference>` on a project that should not have it. Move it to
  the responsibility that owns that technology.
- `graphDiscovery` — almost always a wrong `sourceRoots`. An empty array scans the repository root,
  which suits a flat layout.
- `assemblyDependencyMatrix` reporting "no compiled assemblies" — the projects were not built, or
  `assemblyRoots` points at the wrong folder.

## The three layers

Useful when extending the package rather than consuming it:

- `ArchitecturePolicy` / `ProjectGraph` / `AssemblyGraph` — read the policy, the `.csproj` graph and
  the compiled assembly metadata. No MSBuild and no test-framework types.
- `ArchitecturePolicyEvaluator` — the rules, returning typed `ArchitectureViolation` lists. It takes
  an **in-memory graph**, so a rule is tested as a three-project fixture rather than a repository on
  disk.
- `PurviewArchitectureTests` — the TUnit wrapper, one `[Test]` per rule.

Project files are read because a declared `ProjectReference` is what a reviewer adds and what the
policy governs; compiled assemblies are read separately because they catch what the project file
does not show.

## Overrides

```csharp
[InheritsTests]
public sealed class ArchitectureTests : PurviewArchitectureTests
{
    protected override string? RepositoryRoot => "/path/to/repo";
}
```

`RepositoryRoot` defaults to probing upward from the test binary for the policy file. The search
**stops at the repository boundary** (`.git`) rather than continuing into parent folders — walking
past it would happily find a sibling checkout's policy and validate this repository against someone
else's rules.

## Policy schema version

The current policy document version is **3**. Version 3 adds `typeRules`, `policyIntegrity` and
`waiverPolicy`, all optional, so a version-2 policy remains valid. Version 2 removed the `rules` object
(`noCycles`, `noProductionToTestReferences`, `implementationsMustNotReferenceComposition`,
`siblingImplementationsIndependent`), whose flags were deserialised but never consulted. Use
`SkipRule` or a waiver to opt out of a rule. A version-1 policy that set `rules` must delete the
block; a version-1 policy that did not still validates.
