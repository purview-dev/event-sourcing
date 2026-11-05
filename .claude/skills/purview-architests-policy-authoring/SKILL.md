---
name: purview-architests-policy-authoring
description: Use when authoring or changing .config/architecture-policy.json for Purview.Architecture.Testing - defining responsibilities, categories, mayDependOn and mustNotDependOn, choosing sourceRoots, adding aliases, recording a waiver, setting waiver expiry, or fixing a policyIntegrity failure. Covers the version-3 document, the matrix semantics, and how to validate a policy without running the tests.
license: MIT
metadata:
  product: Purview.Architecture.Testing
  repository: purview-dev/architests
---

# Authoring an architecture policy

The policy is the definition of the architecture. The tests only report what it says, so a vague or
contradictory policy produces a vague or contradictory suite. This skill is about getting the
document right; to *adopt* the package or read a failure, use `purview-architests` instead.

## The shape

`.config/architecture-policy.json` at the repository root:

```json
{
  "$schema": "https://raw.githubusercontent.com/purview-dev/architests/main/architecture-policy.schema.json",
  "version": 3,
  "description": "What this repository's architecture is.",
  "sourceRoots": ["src"],
  "responsibilities": [
    {
      "name": "Core",
      "aliases": ["Domain"],
      "category": "domain",
      "purpose": "The domain model, its invariants and its rules.",
      "mayDependOn": [],
      "mustNotDependOn": ["Application", "Persistence", "Composition"],
      "forbiddenFrameworkReferences": ["Microsoft.AspNetCore.App"]
    }
  ]
}
```

`version` and `responsibilities` are required. Everything else is optional. Version 3 adds
`policyIntegrity` and `waiverPolicy`; both are optional, so a version-2 document remains valid.

## Responsibilities

A project name is `<Responsibility>[.<Implementation>]`, and only the **first segment** is
classified. `Persistence.SqlServer` and `Persistence` are both Persistence: qualification is a
naming choice, never a different set of rules.

| Field | Guidance |
| -- | -- |
| `name` | One PascalCase segment. Name the role, not the technology. |
| `aliases` | Existing names you are not renaming. Prefer renaming and deleting the alias. |
| `category` | `domain`, `application`, `infrastructure`, `composition`, `host` or `shared`. `composition` is the exemption `compositionBoundary` keys on; `host` may also reference it. |
| `purpose` | At least 20 characters, and quoted in failures. Write why the responsibility exists; "misc" is not a purpose. |
| `mayDependOn` | The complete set of permitted references. Silence is not permission. |
| `mustNotDependOn` | Prohibitions that matter. Must not overlap `mayDependOn`. |
| `forbiddenPackagePrefixes` | NuGet prefixes that must never appear, such as `Microsoft.EntityFrameworkCore`. |
| `forbiddenPackageScope` | `own` (default) reports packages the project declares or its own packages pull in; `closure` also reports packages inherited from project references. |
| `forbiddenFrameworkReferences` | Framework prefixes, such as `Microsoft.AspNetCore.App` in a portable layer. |
| `siblingIndependence` | When true, qualified implementations of this responsibility must not reference each other. |

Avoid names that describe a category. `Infrastructure`, `Services`, `Helpers`, `Common`,
`Utilities` and `Managers` are discouraged because nothing can be kept out of them.

## The matrix is three-way

For every declared reference, and for every type reference in the compiled assemblies:

1. **Same responsibility** is always allowed.
2. **`mustNotDependOn`** is a violation.
3. **`mayDependOn`** is permitted.

Anything else is a violation. That is deliberate: an unclassified dependency is a gap in the
policy, not an implicit yes. When a new edge is genuinely intended, add it to `mayDependOn` — that
is a reviewed policy change, which is the point.

## Policy integrity

`policyIntegrity` always runs and cannot be waived. It catches what the schema cannot:

- a matrix entry that names no responsibility (aliases and project names are not names here);
- the same responsibility in both `mayDependOn` and `mustNotDependOn`;
- two responsibilities with the same name;
- an alias equal to a name, or two responsibilities claiming the same alias;
- with `checkReachability`, a prohibition the permitted dependencies can reach anyway. The failure
  prints the path, such as `Core -> Application -> Persistence`, so the edge to remove is obvious.

```json
"policyIntegrity": {
  "checkReachability": true
}
```

Reachability is opt-in because it is the stronger claim: the lists can be consistent while a longer
permitted path still defeats the prohibition.

## Waivers

A waiver is a reviewed departure, not a skip:

```json
"waivers": [
  {
    "project": "Legacy.Utilities",
    "mapsTo": "Shared",
    "rules": ["naming"],
    "reason": "Predates the catalogue; renaming breaks a published package identifier.",
    "reviewedOn": "2026-10-18"
  }
]
```

- `mapsTo` is the useful part: the project keeps its real responsibility, so every dependency rule
  still applies and only the **name** is excused.
- Excuse the narrowest rule that applies. A waiver listing everything is a policy opt-out.
- `policyIntegrity`, `graphDiscovery` and `waiverIntegrity` cannot be waived; naming one is a
  `waiverIntegrity` failure.

### Expiry

```json
"waiverPolicy": {
  "maxAgeDays": 180
}
```

A waiver older than the maximum is a `waiverIntegrity` failure. So are an undated waiver while a
maximum is set, a date that is not `yyyy-MM-dd`, and a date in the future. Expiry is off by default;
adopt it deliberately, and expect to renew or delete the waivers it surfaces.

### Package scope

`forbiddenPackagePrefixes` measures **ownership** by default: what the project declares, what a
shared props file injected, and what its own package references pull in. A package inherited through
a permitted project reference is not owned, so a host is not reported for its adapter’s EF Core.

Use `"forbiddenPackageScope": "closure"` only when the policy means “this technology must not be
anywhere on the compile graph”. That makes hosts responsible for every package their adapters bring
in, and usually requires the adapter to mark implementation packages `PrivateAssets="compile"`.

## Dependency budget

`dependencyBudget` ratchets the project graph: it records the references each project may declare
today, so growth is a reviewed policy change.

```json
"dependencyBudget": {
  "enabled": true,
  "failOnStale": true,
  "entries": [
    { "project": "Application", "references": ["Domain"] },
    { "project": "Persistence.SqlServer", "references": ["Domain", "Application"] }
  ]
}
```

- An entry lists **project names**, exactly as they appear on disk, including the qualification:
  `Persistence.SqlServer`, not `Persistence`.
- A reference outside the entry is a failure. Remove it, or add it in the same reviewed commit that
  adds the `ProjectReference`.
- With `failOnStale` (default true), an allowance the project no longer uses is a failure. Tighten the
  budget rather than leaving a permission nobody needs.
- A project with no entry is not budgeted. Start with `entries: []`, then add one project at a time.
- The rule is not waivable; changing the budget is the sanctioned way to change the graph.

## Validate the policy

```bash
dotnet tool install --global Purview.Architecture.Testing.Cli
architests validate
architests waivers list --stale
```

`validate` runs the schema check and `policyIntegrity` without a build; `waivers list --stale` exits
non-zero when a waiver is due. Exit codes: `0` clean, `1` findings, `2` a malformed command line.
Both commands accept `--repository <path>`.

The test suite runs the same checks and one test per rule, so a policy that passes the tool can
still fail a matrix rule — that failure is an architecture decision, not a document error.

## Checklist

- Every responsibility has a purpose that says what it is for, not what it uses.
- Every dependency that exists is either permitted or forbidden; nothing is left unclassified.
- `mustNotDependOn` entries are not also in `mayDependOn`.
- Names are roles, not categories, and no alias duplicates a name.
- Waivers are narrow, justified, dated, and none is due.
- `policyIntegrity.checkReachability` is on before claiming a prohibition is enforced.
- A version bump is deliberate: adding optional sections does not require one.

