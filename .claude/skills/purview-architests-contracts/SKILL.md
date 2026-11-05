---
name: purview-architests-contracts
description: Use when enabling or debugging Purview.Architecture.Testing's committed-API and port rules - apiSurface, apiDocumentation, portImplementation and portPurity. Covers capturing and reviewing .config/api-surface.json, reading an added or breaking API change, requiring XML documentation, and keeping an inner-layer port implemented and technology-neutral.
license: MIT
metadata:
  product: Purview.Architecture.Testing
  repository: purview-dev/architests
---

# API contracts and ports

Dependency rules say which project may reference which. These rules say what the public surface
actually **is**, and whether the interfaces that define a boundary are honest. They read the compiled
assemblies by metadata only and are off by default, because they need a build.

| Rule | Group | Catches |
| -- | -- | -- |
| `apiSurface` | `apiRules` | The public API differs from the committed baseline |
| `apiDocumentation` | `apiRules` | A public type or member has no XML documentation |
| `portImplementation` | `portRules` | A port in an inner layer has no public implementation |
| `portPurity` | `portRules` | A port's signature names an infrastructure technology |

```json
"apiRules": {
  "enabled": true
},
"portRules": {
  "enabled": true
}
```

Each enabled rule becomes its own TUnit test. A disabled group produces no test, so an absent
`apiSurface` test means the group is off, not that the surface is unchecked.

## The committed baseline

`.config/api-surface.json` records every public type and member, grouped by project, with the
responsibility recorded for review context. It is the contract: a change to it is a change to what a
consumer may call.

- **Captured, never hand-written.** `architests api capture` writes it.
- **Canonical.** Projects, types, interfaces and members are sorted; there are no timestamps; line
  endings are always `\n`. An unchanged API regenerates byte for byte, so a diff means a real change.
- **Committed.** It is reviewed like code. A pull request that changes it explains why.

```bash
dotnet tool install --global Purview.Architecture.Testing.Cli

architests api capture     # write the baseline from the current build
architests api check       # compare; exit 1 on any difference
```

`api check` is the CI gate; `apiSurface` is the same comparison as a test. A missing baseline is
itself an `apiSurface` failure, because the rule would otherwise pass without checking anything.

`api capture` and `api check` accept `--repository <path>` and `--baseline <path>`. Unlike the test
run, a command scans the repository's own build output even when `assemblyRules.assemblyRoots` is
empty, because a tool does not run from a test output directory.

## Reading a baseline failure

The message names the project, the type or member, and whether it was added, removed or changed.

- **Added.** The API grew. Run `architests api capture`, inspect the added entries, and commit them
  with the feature. Additions are not breaking, but they are still a reviewed contract change.
- **Removed.** The message calls it breaking. Restore the member, or capture the baseline as a
  deliberate breaking release and say so in the pull request.
- **Changed.** The shape moved: kind, `sealed`, `abstract`, base type, interfaces, a member's type, or
  whether it is static. Treat the same way as a removal until proven otherwise.

The baseline includes public **nested** types and the SDK's generated `<ProjectName>Assembly` marker.
Both are real public metadata; leave them alone.

## API documentation

`apiDocumentation` reads the XML documentation file the compiler writes beside each assembly.

- `requireTypeDocumentation` (default true) requires a `<summary>` entry for every public type.
- `requireMemberDocumentation` (default false) extends that to every public member.

Member documentation is off by default because a record's positional parameters document its
properties through the type's entry; requiring a separate entry per generated property would punish
a correct record.

A project that ships no XML file is reported once with the fix: set
`<GenerateDocumentationFile>true</GenerateDocumentationFile>`. The Purview SDK does this already.

## The port rules

A **port** is an interface in one of `portRules.categories`, which default to `domain` and
`application`. Those are the layers that own contracts; an interface in `shared` or in an adapter is
not a port by this definition.

### `portImplementation`

Every port must have at least one **public implementation** in a production project. An unimplemented
port is either dead or a boundary nobody crossed; both deserve a review. The dependency matrix still
governs where that implementation may live.

A port implemented only by a consumer (an extension point such as `IArchitectureRule`) fits badly
here: either move it out of the port categories, or record a waiver explaining that the repository
is the contract's publisher, not its implementer.

### `portPurity`

A port's property types, return types and parameter types must not name a forbidden prefix. Defaults
cover `Microsoft.EntityFrameworkCore`, `System.Data`, `Microsoft.AspNetCore` and `System.Net.Http`.
Generic arguments count, so `IReadOnlyList<DbContext>` is caught.

```json
"portRules": {
  "enabled": true,
  "forbiddenTypePrefixes": ["Microsoft.EntityFrameworkCore", "System.Data"]
}
```

A port that names a database or an HTTP client is not a boundary; it is a leak. Move the technology
into the adapter and keep the interface in terms the domain understands.

## Waivers and scope

All four rules are waivable per project and per rule, like the rest of the set:

```json
"waivers": [
  {
    "project": "Legacy.Api",
    "rules": ["apiSurface"],
    "reason": "The published surface is frozen for a major version; the next major captures it.",
    "reviewedOn": "2026-10-18"
  }
]
```

Prefer a narrower setting when the whole repository shares the exception:
`apiRules.requireMemberDocumentation: false`, a different `portRules.categories`, or a longer
`forbiddenTypePrefixes` list.

## CI

```yaml
- run: architests api check
```

`api check` needs a build but no test run. In the test suite, `apiSurface` does the same comparison,
so a repository that runs its architecture tests does not need the command — it is there for a fast
job and for reviewers who want the diff without running TUnit.

## See it working

`src/samples/ArchitectureSample` demonstrates all four rules: `Domain.IOrderRepository` is a port
implemented by `Persistence.InMemory` and `Persistence.Json`, `Application` depends only on the port,
and `.config/api-surface.json` is committed for all six projects.

## Checklist

- `apiRules.enabled` and `portRules.enabled` are deliberate.
- The baseline is committed, canonical, and regenerated after every intended API change.
- A removed or changed member is called out as breaking in the pull request.
- Every public type has a `<summary>`, and the project generates its XML documentation.
- Ports live in an inner layer, are implemented, and name no infrastructure technology.
- A waiver names one project and one rule, with a reason and a review date.

