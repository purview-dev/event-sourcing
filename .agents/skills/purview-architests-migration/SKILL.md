---
name: purview-architests-migration
description: Use when adopting Purview.Architecture.Testing in an existing repository that already has tangled dependencies, or when ratcheting an adopted policy tighter. Covers a permissive first policy, committing the API baseline before enforcing it, recording narrow waivers instead of using SkipRule, starting an empty dependency budget, and tightening one rule at a time without a big-bang rewrite.
license: MIT
metadata:
  product: Purview.Architecture.Testing
  repository: purview-dev/architests
---

# Migrating an existing repository

A policy that describes the architecture you wish you had fails on day one and gets switched off.
Adopt the architecture you **do** have, then tighten it one reviewed step at a time. The rules are
the same either way; only the policy and the waivers change.

## 1. Inventory before writing the policy

- List every production project and what it references. `architests graph` prints the graph once the
  policy can find the projects.
- Group projects into responsibilities that describe what they are for, not where they sit. A
  qualified name (`Persistence.SqlServer`) belongs to `Persistence`; only the first segment counts.
- Name the composition root with `category: "composition"` and hosts with `category: "host"`, so the
  composition boundary works without special cases.

## 2. A permissive first policy

Start with the full dependency closure permitted. The first run should fail only on things you are
ready to fix — usually naming.

```json
{
  "version": 3,
  "sourceRoots": ["src"],
  "responsibilities": [
    {
      "name": "Domain",
      "category": "domain",
      "purpose": "The domain model and its invariants.",
      "mayDependOn": ["Application", "Persistence", "Composition"],
      "mustNotDependOn": []
    }
  ]
}
```

Set `sourceRoots` correctly first: an empty graph makes every rule pass vacuously, which is why
`graphDiscovery` fails instead.

## 3. Add the test class once

```csharp
[InheritsTests]
public sealed class ArchitectureTests : PurviewArchitectureTests;
```

`[InheritsTests]` is load-bearing. Without it the run is green and nothing is checked.

## 4. Record the API before enforcing it

If the public API is already shipped, capture it before enabling `apiRules`:

```bash
architests api capture
```

Then enable the group. The first `apiSurface` run compares against the API you actually have, so it
passes, and every later change becomes a reviewed diff. Do the same for the sample or any package in
the repository.

## 5. Waivers, not skips

When a rule finds a real departure you cannot fix today, record it:

```json
"waivers": [
  {
    "project": "Legacy.Utilities",
    "mapsTo": "Shared",
    "rules": ["naming"],
    "reason": "The package id is published; renaming it is a separate breaking release.",
    "reviewedOn": "2026-10-18"
  }
]
```

- `mapsTo` keeps every dependency rule in force; only the name is excused.
- Excuse one rule, not the whole set.
- Set `waiverPolicy.maxAgeDays` so a waiver has to be renewed. An unreviewed waiver is how a policy
  quietly stops being true.

`SkipRule` removes a rule for the whole repository with no record. Treat it as a last resort, never
as the adoption strategy.

## 6. Start the dependency budget empty

```json
"dependencyBudget": {
  "enabled": true,
  "failOnStale": true,
  "entries": []
}
```

The rule is enabled and checks nothing, which is exactly right: the mechanism is in place. Add an
entry for one project at a time, listing only the references it has today. From then on a new
reference needs a reviewed policy change, and a removed one must be removed from the budget.

## 7. Tighten one step at a time

Each step is a small pull request that keeps the suite green:

1. **Naming.** Add aliases or waivers, then rename projects in a dedicated change.
2. **Forbidden packages.** Add one prefix to one responsibility, fix the violations, repeat.
3. **Dependency direction.** Move a single edge from `mayDependOn` to `mustNotDependOn`, or remove it
   from `mayDependOn` entirely so silence becomes a violation.
4. **Type rules.** Enable `typeRules` for one category with `sealConcreteClasses` and generated-code
   exclusions, fix, then widen the scope.
5. **Ports.** Move an interface into `domain` or `application` only when its implementation lives in
   an adapter and its signature is technology-neutral.
6. **Reachability.** Turn on `policyIntegrity.checkReachability` once the matrix is honest; it will
   find prohibitions the permitted closure bypasses.

A rule that cannot pass even with waivers is telling you the policy is wrong, not the code. Change
the policy.

## Reading a failure during migration

- `naming` — add an alias for an established name, or a waiver with `mapsTo`.
- `dependencyMatrix` / `assemblyDependencyMatrix` — the message names the reference, the direction
  and the likely fix. Prefer moving a type over widening `mayDependOn`.
- `apiSurface` — expected after any intentional API change: run `architests api capture` and review
  the diff before committing.
- `dependencyBudget` — a new reference is over budget; a stale allowance needs removing.
- `policyIntegrity` — the policy contradicts itself; fix the policy before the code.

## Checklist

- `sourceRoots` finds the projects, and `graphDiscovery` passes.
- The matrix describes today's graph, not the target graph.
- The API baseline is committed before `apiRules` is switched on.
- Every departure is a narrow, dated waiver rather than a skip.
- The dependency budget starts empty and grows one reviewed entry at a time.
- Each tightening step is its own pull request, with the suite green.
