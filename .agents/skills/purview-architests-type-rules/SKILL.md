---
name: purview-architests-type-rules
description: Use when enabling or debugging Purview.Architecture.Testing's type-level rules - typeNaming, typeShape, typeNamespace and attributePolicy. Covers the typeRules policy section, which public types are governed, why a type is reported as unsealed or outside its root namespace, how generated code and nested types are treated, and how to fix or waive each finding.
license: MIT
metadata:
  product: Purview.Architecture.Testing
  repository: purview-dev/architests
---

# Type-level naming, shape and namespace rules

These rules police the **public surface** a project compiles, not its dependencies. They read the
compiled assembly by metadata only: no assembly is loaded, no code runs, and no source file is
needed. Enable them when the API a consumer receives matters as much as the direction its
references run.

For dependency direction, use the `purview-architests` skill. This skill covers `typeNaming`,
`typeShape`, `typeNamespace` and `attributePolicy`.

## Enable them

They are off by default, because they need a build to have produced the assemblies:

```json
{
  "assemblyRules": { "enabled": true },
  "typeRules": {
    "enabled": true,
    "appliesTo": { "categories": ["domain", "application", "shared"] },
    "excludeGeneratedCode": true
  }
}
```

- An empty `appliesTo.categories` governs **every production responsibility**.
- Test projects are never governed.
- `assemblyRules` and `typeRules` scan the same assemblies. `assemblyRoots` and `assemblyGlobs` live
  under `assemblyRules` and apply to both; an empty `assemblyRoots` scans the test output directory.
- Only **public** types are checked, including public nested types. Internal and private types are
  outside the API and are left alone.

## What the four rules catch

| Rule | Catches |
| -- | -- |
| `typeNaming` | An interface without `I`, an attribute without `Attribute`, an exception without `Exception`, a discouraged suffix (`Manager`, `Helper`, `Util`, `Impl`) |
| `typeShape` | An unsealed public class, a public instance field, a mutable collection return, a misnamed exception, a utility class that is not static |
| `typeNamespace` | A public type outside the project's `RootNamespace` |
| `attributePolicy` | A missing required attribute, a forbidden attribute, `[Obsolete]` without a message, `InternalsVisibleTo` naming production code |

Each enabled rule becomes its own TUnit test. A disabled group produces **no** type-rule test rather
than a test that checks nothing, so a green run cannot mean "the group was off".

## Governing a subset

`appliesTo.categories` is the usual scope. A composition root is often excluded because it is allowed
to reference everything:

```json
"appliesTo": { "categories": ["domain", "application", "infrastructure", "shared"] }
```

Generated code is skipped by default. A source generator must not be forced to change its output:

```json
"excludeGeneratedCode": true,
"excludeNamespacePrefixes": ["Contoso.Generated"]
```

A type marked `[GeneratedCode]` or `[CompilerGenerated]` is skipped, as is any type under an excluded
namespace prefix.

## Type naming

- An interface name must start with `I` followed by an uppercase letter: `IOrderRepository`.
- A class deriving from `System.Attribute` must end with `Attribute`.
- A class deriving from an exception must end with `Exception`.
- A type ending with a configured `discouragedTypeSuffixes` entry is reported. The default list
  names mechanisms, not responsibilities. Configure it to match the repository's vocabulary.

Generic arity is removed before the check, so `IRepository<T>` is checked as `IRepository`.

## Type shape

- **Unsealed public class.** A public concrete class must be `sealed` unless it is `abstract`. An
  unsealed class is a public extension point nobody declared; seal it, or make the base type explicit.
- **Public instance field.** Constants and static readonly fields are fine; an instance field cannot
  validate or evolve without a breaking change.
- **Mutable collection return.** A public property or method returning `List<T>` or
  `Dictionary<TKey, TValue>` hands the caller the owner's state. Return `IReadOnlyList<T>` or
  `IReadOnlyDictionary<TKey, TValue>`.
- **Misnamed exception.** A class named `...Exception` must derive from `System.Exception`.
- **Utility class.** A class with only static members and no public constructor must be declared
  `static`, so it cannot be instantiated by accident.

Turn off either check in the policy when it does not fit:

```json
"sealConcreteClasses": false,
"requireReadOnlyCollections": false
```

## Type namespace

The rule compares the type's declared namespace with the project's `RootNamespace`, read from the
`.csproj`: the namespace must equal it or start with it followed by a dot.

Metadata records the namespace, not the file path, so the rule **cannot** check that the folder
matches. It deliberately makes no such claim.

Set `RootNamespace` explicitly when it differs from the project name:

```xml
<RootNamespace>Purview.Architecture.Testing</RootNamespace>
```

Without it, the project name is used, which is the SDK's own fallback.

## Attribute policy

| Setting | Default | Effect |
| -- | -- | -- |
| `requiredAttributes` | `[]` | Namespace-qualified attributes every governed public type must carry |
| `forbiddenAttributes` | `[]` | Namespace-qualified attributes no governed public type may carry |
| `allowObsoleteWithoutMessage` | `false` | Permit `[Obsolete]` with no message |
| `forbidInternalsVisibleToProduction` | `true` | Reject `InternalsVisibleTo` naming a production assembly |

`[Obsolete("Use IOrderStore instead.")]` is fine. A bare `[Obsolete]` is reported: a caller is told
the type is going away but not what to do about it.

`InternalsVisibleTo` is checked at assembly level. A friend that maps to a **test** project is
ordinary; a friend that maps to a **production** project is a boundary leak, because the referenced
project can now see internals it was never meant to depend on. Expose a public abstraction instead.

## Fix or waive

Fix the type when the rule is right; rename it, seal it, return a read-only interface, or move it
under the root namespace.

A waiver is per project and per rule, and the project field is the **project name** the assembly
belongs to:

```json
"waivers": [
  {
    "project": "Persistence",
    "rules": ["typeShape"],
    "reason": "The EF entity must stay inheritable for the proxy; sealing it breaks materialisation.",
    "reviewedOn": "2026-10-18"
  }
]
```

Prefer the narrow settings when the whole repository shares an exception: `excludeNamespacePrefixes`
for generated code, `sealConcreteClasses` for a codebase that uses inheritance as a feature, or
`appliesTo.categories` to leave the composition root out.

## Troubleshooting

- **No type-rule tests at all.** `typeRules.enabled` is false, or the policy failed to load. The
  group produces no test rather than a vacuous pass, so this is visible in the test list.
- **A rule reports nothing and you expected findings.** The projects were not built, or the
  assembly was not in the scan root. Type rules use `assemblyRules.assemblyRoots`; an empty value
  scans the test output directory, so the test project must reference the projects it governs.
- **A type you expected is missing.** It is internal, nested inside an internal type, marked as
  generated, under an excluded namespace, in a test project, or in a responsibility outside
  `appliesTo.categories`.
- **`typeNamespace` reports everything.** The project has no `RootNamespace` and its namespace is
  not the project name. Set `RootNamespace` explicitly.
- **A public API baseline is also enabled.** Type-rule findings are API-shape findings; fix them
  before regenerating a baseline, or the baseline records the problem.

## See it working

`src/samples/ArchitectureSample` enables `typeRules` for the `domain`, `application`, `infrastructure`
and `shared` categories. Its projects declare `RootNamespace` explicitly, its classes are sealed, its
collections are exposed as `IReadOnlyList<T>`, and `IOrderRepository` follows the interface
convention.

## Checklist

- `typeRules.enabled` is true and the categories are deliberate.
- Every governed public class is sealed or abstract, unless `sealConcreteClasses` is off.
- No public instance fields or mutable collection returns are exposed.
- `RootNamespace` is set on projects whose namespace differs from their name.
- Generated code is excluded by attribute or namespace, not by loosening the rule.
- Every waiver names one project and one rule, with a reason and a review date.

