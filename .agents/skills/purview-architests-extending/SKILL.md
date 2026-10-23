---
name: purview-architests-extending
description: Use when adding a bespoke rule to Purview.Architecture.Testing, implementing IArchitectureRule, or working on the package's internals - the three-layer design, the project graph, the metadata-only assembly reader, or the evaluator. Covers the ArchitectureContext, what an additional rule gets for free, and testing a rule against an in-memory fixture.
license: MIT
metadata:
  product: Purview.Architecture.Testing
  repository: purview-dev/architests
---

# Extending Purview.Architecture.Testing

A repository can add a bespoke check without a second architecture framework. This skill covers the
extension point and the internals a rule is built on. To *consume* the package — adopting it,
writing a policy, reading a failure — use the `purview-architests` skill instead.

## Add a rule

Implement `IArchitectureRule` and list it in `AdditionalRules`:

```csharp
using Purview.Architecture.Testing;

[InheritsTests]
public sealed class ArchitectureTests : PurviewArchitectureTests
{
    protected override IReadOnlyList<IArchitectureRule> AdditionalRules => [new NoConsoleLoggingRule()];

    sealed class NoConsoleLoggingRule : IArchitectureRule
    {
        public string Rule => "noConsoleLogging";

        public IReadOnlyList<ArchitectureViolation> Evaluate(ArchitectureContext context) =>
            context.Projects.Values
                .Where(project => !project.IsTestProject)
                // ...inspect the project or its assembly...
                .Select(project => new ArchitectureViolation(Rule, project.Name, "Console logging is banned here."))
                .ToArray();
    }
}
```

The `Rule` id is the key everywhere: it names the generated test, it is what `SkipRule` receives, and
it is what a waiver's `rules` array matches.

## What the context gives you

`ArchitectureContext` carries the three inputs a rule can need:

| Member | What it is |
| -- | -- |
| `Policy` | The parsed `ArchitecturePolicy`, so a rule reads the same definition the built-in rules do. |
| `Projects` | The project graph, keyed by project name, with declared references, packages, framework references and analyzers. |
| `Assemblies` | The compiled assemblies, keyed by project name, each with the external types it references. Empty when `assemblyRules.enabled` is false. |

`ArchitecturePolicy.Classify(projectName)` is the classification every built-in rule uses — the first
segment of the project name, honouring aliases and a waiver's `mapsTo`. Use it so a custom rule
agrees with the standard set.

## What an additional rule gets for free

- **Its own `[Test]`.** Each rule id becomes a case of
  `AdditionalRule_GivenTheRepository_RespectsThePolicy`, so a failure names the rule rather than
  collapsing into one assertion.
- **`SkipRule`.** Override `SkipRule` and return `true` for the rule id.
- **Waivers.** A waiver whose `rules` names the rule id is honoured automatically, so a reviewed,
  per-project departure needs no code.
- **Reporting.** `ArchitecturePolicyEvaluator.Report` renders the violations, and the failure's
  `.Because(...)` lists every one.

A violation's `Project` must be the project name (or the offender a waiver can name), and its `Rule`
must be the rule id, or the waiver filter cannot match it.

## Test the rule against a fixture

The evaluator takes an in-memory graph, so a rule is tested as a small fixture rather than a
repository on disk — exactly how the package's own rules are tested:

```csharp
var evaluator = new ArchitecturePolicyEvaluator(
    policy,
    projects,
    assemblies,
    [new NoConsoleLoggingRule()]
);

await Assert.That(evaluator.EvaluateAdditionalRule("noConsoleLogging")).IsEmpty();
```

`ArchitecturePolicyEvaluator` also has a two-argument constructor for project-only rules, and a
four-argument one when the rule needs the assembly graph.

## The three layers (non-negotiable)

Keep the separation, because it is what makes a rule testable:

- **`ArchitecturePolicy` / `ProjectGraph` / `AssemblyGraph`** — read the policy, the `.csproj` graph
  and the compiled assembly metadata. **No MSBuild and no test-framework types.**
- **`ArchitecturePolicyEvaluator`** — the rules, returning typed `ArchitectureViolation` lists. It
  takes an **in-memory graph**, so a rule is testable as a small fixture.
- **`PurviewArchitectureTests`** — the TUnit wrapper, one `[Test]` per rule.

Every new rule must be evaluable from an in-memory fixture and asserted by a small
`ArchitectureTesting.UnitTests` test.

## Reading assemblies

- Read compiled assemblies by **metadata only** (`System.Reflection.Metadata`). Never `Assembly.Load`
  or any API that executes code or needs a probing path.
- The `TypeReference` table is the dependency set: base types, interfaces, generic arguments,
  signatures, attributes and method-body uses all appear there.
- Attribute an assembly to a project by the SDK's generated `<ProjectName>Assembly` marker, falling
  back to an exact project-name match. An assembly that maps to no project is a package and is
  ignored — a package is never a project.
- Resolve references against the set of assemblies actually scanned, never by name guessing.

## Repository conventions

- TUnit on Microsoft.Testing.Platform. No xUnit, NUnit, MSTest or FluentAssertions.
- Tests use AAA and `Subject_Scenario_Expectation` naming, and must pass on Windows, Linux and macOS.
- `architecture-policy.schema.json` at the repository root is both the published schema and the
  resource embedded in the package. Never keep a second copy.
- The repository applies its own package to itself (`src/tests/Repository.UnitTests`), with the
  sample at `src/samples/ArchitectureSample` showing consumer adoption.
