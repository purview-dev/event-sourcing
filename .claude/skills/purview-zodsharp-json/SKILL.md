---
name: purview-zodsharp-json
description: >-
  Use when combining Purview.ZodSharp with JSON - deserialize-and-validate and validating converters
  for System.Text.Json or Newtonsoft.Json, choosing between the two packages, exporting a schema to
  JSON Schema with Z.ToJsonSchema, importing one with Z.FromJsonSchema, and sharing a contract with
  TypeScript Zod.
license: MIT
metadata:
  product: Purview.ZodSharp
  repository: purview-dev/zodsharp
---

# JSON and JSON Schema

Two integration packages, and they are **mutually exclusive in practice** — pick the one matching
your JSON library:

```bash
dotnet add package Purview.ZodSharp.SystemTextJson
dotnet add package Purview.ZodSharp.NewtonsoftJson
```

Each adds deserialize-and-validate, a validating converter, and JSON Schema **import**. JSON Schema
**export** is in the core package.

Both can be referenced from one project without `extern alias` — the JSON Schema types live in
`ZodSharp.JsonSchema.SystemTextJson` / `ZodSharp.JsonSchema.NewtonsoftJson` — but the
deserialize/serialize extension overloads share names, so **import exactly one package namespace per
file**.

## Deserialize and validate

```csharp
using ZodSharp;

var userSchema = Z.Object()
    .Field("name", Z.String().Min(3))
    .Field("age", Z.Number().Min(0).Int())
    .Build();

var result = userSchema.DeserializeAndValidate("""{ "name": "John", "age": 30 }""");

await using var stream = File.OpenRead("user.json");
var streamed = await userSchema.DeserializeAndValidateAsync(stream);
```

Going the other way:

```csharp
var json = userSchema.ValidateAndSerialize(user);              // ValidationResult<string>
await userSchema.ValidateAndSerializeAsync(user, outputStream);
```

Failures add `deserialization_failed` and `json_error` codes alongside the schema's own.

## Validating converters

```csharp
// System.Text.Json
var options = new JsonSerializerOptions { Converters = { userSchema.CreateValidatingConverter() } };
var value = JsonSerializer.Deserialize<User>(json, options);

// Newtonsoft.Json
var value2 = JsonConvert.DeserializeObject<User>(json, userSchema.CreateValidatingConverter());
```

Invalid JSON **throws** — `JsonException` (STJ) or `JsonSerializationException` (Newtonsoft), with a
`"Validation failed: …"` message. Each converter removes itself from the options/serializer it uses
internally, to avoid recursing into itself.

## Which package differs how

| Aspect | SystemTextJson | NewtonsoftJson |
| --- | --- | --- |
| Async result | `ValueTask<…>` | `Task<…>` |
| Options parameter | `JsonSerializerOptions` | `JsonSerializerSettings` |
| Converter return | generic `JsonConverter<T>` | non-generic `JsonConverter` |
| `JToken` overload | no | yes |
| Formatting | via options | explicit `Formatting` argument |
| Invalid-data exception | `JsonException` | `JsonSerializationException` |
| Plumbing type | `JsonElement` | `JToken`/`JObject`/`JArray` |

## JSON Schema export

`Z.ToJsonSchema` (core package) produces a Draft 2020-12 `JsonSchemaDefinition`:

```csharp
var jsonSchema = Z.ToJsonSchema<Dictionary<string, object?>>(userSchema, new ToJsonSchemaOptions
{
    Title = "User",
    Id = "https://example.com/schemas/user.json",
});
```

`ToJsonSchemaOptions`: `IncludeSchema` (default `true`, emits `$schema`), `Id`, `Title`.

`JsonSchemaDefinition` (`ZodSharp.JsonSchema`) is a mutable POCO mirroring the keywords — identity
(`$schema`/`$id`/`$ref`), type and title, string/number/array/object constraints, composition
(`anyOf`/`oneOf`/`allOf`), `enum`/`const`, `$defs` and draft-07 `definitions`, and the metadata
keywords including OpenAPI 3.0 `nullable`.

Covered schema types: `ZodString`, `ZodNumber`, `ZodBoolean`, `ZodNull`, `ZodObject`, `ZodOptional`,
`ZodUnion`, `ZodArray`, `ZodLiteral`, `ZodNullable`, `ZodNativeEnum`/`ZodEnum`, `ZodLazy`. Notable
behaviours:

- objects emit `additionalProperties: false`;
- literals emit `const` plus `type`;
- native enums emit `enum` in their System.Text.Json wire form — numbers by default, member names
  when serialized as strings. A `[Flags]` enum emits `type: integer` instead, because a combination is
  valid without being a defined member;
- lazy/recursive schemas emit `$ref` entries under `$defs` (`#/$defs/__lazyN`);
- rules that describe their own constraint implement `IJsonSchemaRule` and are applied during export;
- `[NonSentinel]` exports exact patterns for `DateOnly`, `TimeOnly`, `DateTime`, and `DateTimeOffset`. The
  default `DateTimeOffset` pattern is offset-aware and needs no converter. An optional narrower UTC contract
  uses `ZodSharpDateTimeOffsetSerialization=Utc` or
  `[assembly: ZodSharpSchemaDefaults(DateTimeOffsetSerialization = DateTimeOffsetSerialization.Utc)]`, paired
  with the integration package's `UtcDateTimeOffsetConverter` (which rejects non-UTC input on read). Override
  per type/property with `[DateTimeOffsetBehavior(...)]`.

### Exporting a generated `[ZodSchema]`

No need to rebuild the rules by hand with `Z.Object()`:

```csharp
var jsonSchema = RepositoryInventoryItemSchema.BuildJsonSchema();
var exported = Z.ToJsonSchema(new RepositoryInventoryItemSchemaValidator());
```

The definition comes from the same metadata the generated validator uses, so the two cannot drift.
Things worth knowing:

- **Property names** use the System.Text.Json wire form: `[JsonPropertyName]` wins, otherwise the name
  is camelCased to match `JsonSerializerDefaults.Web`. A non-camelCase global naming policy configured
  at runtime is invisible to the generator, so adjust the exported names or declare
  `[JsonPropertyName]`.
- **Required** follows the nullable reference-type annotation: a non-nullable reference type (or
  `Nullable<T>`) is required; a nullable one is wrapped in `anyOf: [ …, { "type": "null" } ]` and is
  not required. `[Required]` also makes a field required, and a rule that rejects an absent value —
  `[RequiredZod]` — makes the field non-null even with a nullable annotation. The generated validator
  agrees with all of this.
- **Scalar value objects** (`[Scalar<TValue>]`) export as the **underlying** value's schema, not as an
  object wrapping a `Value` property. A `Guid` scalar exports as a canonical 8-4-4-4-12 hexadecimal
  `pattern`, not Zod's RFC `uuid` format, because a C# `Guid` accepts any 128-bit value.
- **Collection items** follow the null-items setting: `T[]`/`List<T>` maps to `z.array(T)` and
  `T?[]`/`List<T?>` (or a property allowing nulls via `[CollectionBehavior(AllowNulls = true)]`) to
  `z.array(T.nullable())`. A collection whose elements may be null exports
  `items.anyOf: [ …, { "type": "null" } ]`; by default the generated validator rejects a null element with
  `invalid_type` at the element's path, matching `z.array(T)`.
- **Dictionaries map to `z.record(z.string(), V)`.** A property implementing `IDictionary<,>` or
  `IReadOnlyDictionary<,>` exports `{ "type": "object", "additionalProperties": <V> }` (a nullable value
  wraps that value schema in a `null` branch), and its size constraints use `minProperties`/`maxProperties`.
  `Z.Record(schema)` exports the same shape, and `Z.FromJsonSchema` reads an object with
  `additionalProperties` back as a record in both JSON integrations.

## JSON Schema import

```csharp
using ZodSharp;
using ZodSharp.JsonSchema.SystemTextJson;   // or …NewtonsoftJson

var userSchema = Z.FromJsonSchema(jsonSchemaString);
var result = userSchema.Validate(userData);
```

Overloads take a `string` or an already-deserialized `JsonSchemaDefinition`, and both return
`IZodSchema<object, object>`.

`Z.FromJsonSchema` is a **C# 14 extension member on `Z`**, so it only exists when a JSON integration
package is referenced. `Z.ToJsonSchema` is a real static member in the core package.

Mapped keywords:

- `type` — `string`, `number`, `integer`, `boolean`, `null`, `object`, `array`.
- `enum` → a union of literals (a single member becomes a literal); `const` → a literal.
- `anyOf`/`oneOf` → union; `allOf` → the intersection of every branch, plus the base schema when the
  schema also declares a `type`. A type-less branch is `any`, matching Zod's `fromJSONSchema`.
- Strings — `minLength`, `maxLength`, `pattern`, and `format` (`email`, `uri`, `uuid`). The
  `uuid`/`guid` format maps to the versionless `.UUID()`; a versioned `.UUID(UuidVersion.V7)` schema
  exports `format: "uuid"` plus a version-pinning `pattern`, and the `allOf` composition preserves it
  on import. Multiple `pattern` constraints compose under `allOf`, and every branch is applied.
- Numbers — `minimum`, `maximum`, `multipleOf`; `integer` additionally applies `.Int()`.
- Objects — `required` and optional fields. Arrays — `items`, `minItems`, `maxItems` (a nullable item
  schema — `anyOf` with a `{"type":"null"}` branch — imports as a nullable element).

### Limits

`$ref` resolves **local** references only (`#/…`, for example `#/$defs/Name`). An external target
throws `NotSupportedException` naming it — inline the referenced schema, or move it under the root
`$defs`, before importing. Local references **may be cyclic**: one still being resolved becomes a lazy
schema.

The reader binds `$schema`, `$id`, `$ref`, `$defs` and the draft-07 `definitions` through the
integration package's `JsonSchemaSerializerOptions`, and the same options write them back, so exported
definitions round-trip through either package. `JsonSchemaDefinition` itself carries no serializer
annotations — serialize it with `JsonSchemaSerializerOptions` to get keyword-compliant output.

## Sharing a contract with TypeScript Zod

```typescript
import { z } from "zod";
const UserSchema = z.object({ username: z.string().min(3), email: z.string().email() });
const jsonSchema = z.toJSONSchema(UserSchema);   // Zod v4+
```

```csharp
var userSchema = Z.FromJsonSchema(jsonSchemaString);
```

This repository also proves agreement by fixture rather than by assertion. `bun run generate-fixtures`
runs `src/ts/generate-fixtures.ts`, writing `src/ts/fixtures/*.json` plus a `manifest.json` in which
**Zod is the authority** on each fixture's validity. `just test` then reads both, asserts the C#
outcome matches, and writes validated C# output to
`src/tests/cross-platform/output/{systemtext,newtonsoft}-valid.json`. Finally `bun run test` (vitest)
re-checks the fixtures under Zod and parses the C# output — closing the loop in both directions.

If the C# output directory is empty, the vitest suite tells you to run the C# cross-platform tests
first.

## Related skills

`purview-zodsharp-core` for the schema API, and `purview-zodsharp-source-generator` for the generated
validators that export through `BuildJsonSchema`.
