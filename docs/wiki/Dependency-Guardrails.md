# Dependency Guardrails

This page documents repository guardrails that prevent known dependency/runtime pitfalls.

## Purview.ZodSharp direct-reference guardrail

### Problem

When a consumer project directly references the `Purview.EventSourcing.Validation.ZodSharp` project and uses `Purview.ZodSharp`
types, relying on transitive package flow can lead to runtime assembly load failures (for example,
`FileNotFoundException` for `Purview.ZodSharp`).

### Required fix in consuming project

Add a direct package reference:

```xml
<PackageReference Include="Purview.ZodSharp" />
```

### Automated enforcement

`Purview.EventSourcing.Validation.ZodSharp` includes a build target in package `buildTransitive` assets
(`buildTransitive/Purview.EventSourcing.Validation.ZodSharp.targets`):

- Target name: `ValidateZodSharpDirectReference`
- Runs: `BeforeTargets="ResolveReferences"`
- Behavior:
  - Detects projects that reference `Purview.EventSourcing.Validation.ZodSharp` via `ProjectReference`
  - Fails the build if `PackageReference Include="Purview.ZodSharp"` is missing
  - Emits a remediation message with the exact package reference to add

This shifts failure left from runtime to build-time.

### CI verification

The reusable pack workflow also validates the generated `.nupkg` and fails if
`buildTransitive/Purview.EventSourcing.Validation.ZodSharp.targets` is missing from the package contents.

## Validation adapters overview

- `Purview.EventSourcing.Validation.FluentValidation`: adapter for `FluentValidation.IValidator<T>` to
  `IAggregateValidator<T>`.
- `Purview.EventSourcing.Validation.ZodSharp`: adapter for `Purview.ZodSharp` schema validation to `IAggregateValidator<T>`.

When using either adapter package directly from source projects, keep direct package references explicit for external
runtime dependencies used by the adapter.

## Admin API validation and OpenAPI dependencies

`Purview.EventSourcing.Admin.API` validates its request contracts and options with Purview.ZodSharp source-generated schemas and
ships the Admin API OpenAPI document (`/openapi/admin.json`) used to generate `Purview.EventSourcing.Admin.Client`. As a
result `Purview.ZodSharp`, `Purview.ZodSharp.AspNetCore`, and `Purview.ZodSharp.SystemTextJson` are direct dependencies of the Admin API
package.

### OpenAPI XML-comment source generator is disabled in Admin.API

The `Microsoft.AspNetCore.OpenApi` package ships a source generator that builds a runtime cache of XML doc IDs across
the compilation and its referenced assemblies. Purview's telemetry scaffolding (`Purview.Telemetry.SourceGenerator`)
re-declares the same attribute types in every assembly, which makes that cache throw at runtime with a duplicate key
when an OpenAPI document is generated. The Admin.API project therefore removes the
`Microsoft.AspNetCore.OpenApi.SourceGenerators` analyzer from its compilation (see `Admin.API.csproj`), and the
spec-export tool (`src/tools/AdminAPI.OpenAPI`) does not feed referenced assembly XML docs to the generator. The
generated Admin API document and typed client remain complete; XML-comment-derived schema descriptions are omitted.
