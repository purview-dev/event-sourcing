---
name: purview-value-objects-efcore
description: >-
  Use when persisting Purview.ValueObjects with Entity Framework Core - ConfigureValueObjects,
  UseValueObjects, scalar-to-column conversion, complex-type versus JSON mapping, value objects as
  keys and foreign keys, query translation, generated key values, migrations, and the VO1009-VO1021
  Entity Framework diagnostics.
metadata:
  product: Purview.ValueObjects
  repository: purview-dev/value-objects
---

# Value objects and Entity Framework Core

`Purview.ValueObjects` source-generates the EF Core mapping **into the consuming project**, and only
when that project references `Microsoft.EntityFrameworkCore`. The runtime package itself carries no
EF dependency, so a domain assembly stays free of Entity Framework while the persistence assembly
gets the converters, comparers and conventions.

Everything is opt-in per feature with a three-level opt-out: the `DisableValueObjectsEFGeneration`
MSBuild property, then assembly defaults (the attribute, or the `ValueObjectsDefaultEFMapping`,
`ValueObjectsDefaultGenerateEFConverter`, `ValueObjectsDefaultGenerateEFComparer` and
`ValueObjectsDefaultGenerateEFValueGenerator` properties), then per-type options.

## Wiring it up

Two ways, pick one:

```csharp
// 1. In the context.
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigureValueObjects();
}
```

```csharp
// 2. At registration - no OnModelCreating override needed.
services.AddDbContext<ShopContext>(options =>
    options.UseSqlServer(connectionString).UseValueObjects());

services.AddDbContextFactory<ShopContext>(options =>
    options.UseSqlite("Data Source=shop.db").UseValueObjects());
```

`ConfigureValueObjects` is emitted into the `Microsoft.EntityFrameworkCore` namespace — the same
namespace as `ModelBuilder` — so no extra `using` is needed. `UseValueObjects()` registers a
generated `ModelCustomizer` that runs it after `OnModelCreating`, and works for `AddDbContext`,
`AddDbContextFactory`, `AddDbContextPool` and manual construction.

## What gets mapped

| Shape | Maps to | Notes |
| --- | --- | --- |
| `[Scalar]` | one column of the underlying primitive | via `{Type}.EF.Converter` + `ValueComparer`; `EmailAddress` is a `TEXT` column |
| `[ValueObject]` (default) | an EF Core complex type — one column per member | EF Core 8+ only; nested scalars convert too |
| `[ValueObject(EFMapping = EntityFrameworkMapping.Json)]` | a single JSON column | a value converter, so it works on every EF Core version |

The provider-to-model direction always goes through `Hydrate(...)`. **Materialization is a replay
path**: neither `OnValidate` nor a ZodSharp schema runs when a row is read. That matches
`ValueObjectDeserializationMode.Hydrate`, and `Strict` applies to the JSON wire format, not to the
database — validate before persisting, or enforce the invariant in the schema.

An enum-backed scalar converts through the enum's **integral** type (`ValueConverter<OrderStatus, int>`).
Leaving the enum as the provider type makes EF compose its own enum-to-number converter with the
generated one, and the composite loses the provider tolerance that raw-primitive comparisons rely on.

A plain `enum` property that is not a value object keeps EF Core's own enum mapping, untouched.

## Querying — no `.Value`

Because a scalar converts to its primitive column, a predicate compares the value object directly:

```csharp
EmailAddress email = EmailAddress.Create("demo@example.com");

var customers = await db.Customers
    .Where(c => c.Email == email)              // translates to [email] = @p
    .ToListAsync();
```

Complex value objects map as complex types, so their members are queryable:

```csharp
var orders = await db.Orders
    .Where(o => o.Total.Amount > 20m)
    .Where(o => o.Total.Currency == CurrencyCode.Create("USD"))
    .ToListAsync();
```

A property may also be compared to the **raw underlying value** — `c.Email == "demo@example.com"`,
`m.Id == guid` — because every generated converter accepts either shape. Without that, EF hands the
raw provider value to the converted property's built-in converter, which coerces with
`Convert.ChangeType` and throws for provider types that do not implement `IConvertible` (`Guid`,
`DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly`) or cannot be converted at all (strings). See
[dotnet/efcore#32030](https://github.com/dotnet/efcore/issues/32030).

Compiled models (`dotnet ef dbcontext optimize`) keep that tolerance because every generated
converter exposes a `JsonValueReaderWriter`-taking constructor and a `JsonReaderWriter` property,
which is what EF's design-time generator looks for. That detection is an undocumented EF
implementation detail: if it changes, compiled models fall back to the built-in converter and only
raw-primitive comparisons break.

## Keys, foreign keys and indexes

A value object key needs no special handling — the converter maps it to its primitive column, and
convention still makes `{Type}Id` the primary key:

```csharp
sealed class Customer
{
    public CustomerId Id { get; set; }          // primary key, stored as uniqueidentifier
    public TenantId TenantId { get; set; }      // foreign key to Tenant.Id
    public Tenant Tenant { get; set; } = default!;
}
```

Column facets are configured on the value object property and apply to the converted column, exactly
as for a primitive:

```csharp
public sealed class TenantConfiguration : IEntityTypeConfiguration<TenantEntity>
{
    public void Configure(EntityTypeBuilder<TenantEntity> builder)
    {
        builder.Property(entity => entity.Id).ValueGeneratedNever();   // when the domain owns the key
        builder.Property(entity => entity.Key).HasMaxLength(100).IsRequired();
        builder.HasIndex(entity => new { entity.TenantId, entity.Key }).IsUnique();
    }
}
```

The converter is expression-based, so composite indexes, unique constraints and `HasQueryFilter`
behave like their primitive equivalents:

```csharp
modelBuilder.Entity<Invoice>().HasQueryFilter(invoice => invoice.TenantId == currentTenant.TenantId);
```

**Complex types cannot be keys.** An identity spanning two or more members is awkward as a complex
type: either flatten the members onto the entity and put a unique index over them, or store the
value object as JSON and index the derived columns you actually query.

## Generated key values

A Guid-backed scalar can generate its own time-ordered keys without a database round trip. Opt in,
register one convention, and pick the ordering the store needs:

```csharp
[Scalar(GenerateEFValueGenerator = true)]
public readonly partial record struct CustomerId { public Guid Value { get; } }

protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    base.ConfigureConventions(configurationBuilder);
    configurationBuilder.UseValueObjectKeyGenerators();                       // version 7
    // configurationBuilder.UseValueObjectKeyGenerators(ValueObjectKeyOrdering.SqlServer);
}
```

Ordering matters: SQL Server's `uniqueidentifier` compares the trailing six bytes first, so plain
version 7 values are effectively random there and a clustered key fragments. Full detail — the
ordering table, the public `ValueObjectSequentialGuid` helper, and the rules about when EF invokes a
generator — is in [references/key-value-generation.md](references/key-value-generation.md).

Value generation is Guid-backed scalars only, and requires the EF converter; anywhere else reports
`VO1021`.

## Value objects in other assemblies

| Declaring assembly | What it emits | How the consumer discovers it |
| --- | --- | --- |
| References EF Core | a nested `EF` class and an `IEFScalarValueObject`/`IEFComplexValueObject` marker per type | through the marker interfaces |
| Does **not** reference EF Core | nothing EF-related | through the `[Scalar]`/`[ValueObject]` attributes; converters are generated **inline** in the consumer's registry |

Either way the consuming project's registry maps them, so a shared `EmailAddress` is converted on
your entities without being configured twice.

A provider assembly that only *defines* value objects and owns no `DbContext` should opt out of its
own registry, so consumers do not get duplicate `ValueObjectEFExtensions`/`ValueObjectModelCustomizer`
types in the `Microsoft.EntityFrameworkCore` namespace:

```xml
<PropertyGroup>
    <DisableValueObjectsEFRegistry>true</DisableValueObjectsEFRegistry>
</PropertyGroup>
```

A complex value object is discovered across assemblies when it opted into a mapping or a comparer
(including the default `ComplexType` mapping with `GenerateEFComparer = false`). Only one that opted
out entirely — `EFMapping = None` **and** `GenerateEFComparer = false` — is not discovered; configure
that one on the entity by hand.

## Configuring a property explicitly

The nested `EF` class is there when you want the registry's work done by hand:

```csharp
builder.Entity<Customer>()
    .Property(c => c.Email)
    .HasConversion(EmailAddress.EF.Converter, EmailAddress.EF.Comparer);

builder.Entity<Order>()
    .ComplexProperty(o => o.Total, money =>
    {
        money.Property(m => m.Amount);
        money.Property(m => m.Currency).HasConversion(CurrencyCode.EF.Converter, CurrencyCode.EF.Comparer);
    });
```

## Opting out

```csharp
// Per type.
[Scalar(GenerateEFConverter = false, GenerateEFComparer = false)]
public readonly partial record struct InternalCode { }

[ValueObject(EFMapping = EntityFrameworkMapping.Json)]
public readonly partial record struct Audit { }

[ValueObject(EFMapping = EntityFrameworkMapping.None, GenerateEFComparer = false)]
public readonly partial record struct Notes { }
```

```csharp
// Per assembly.
[assembly: ValueObjectDefaults(EFMapping = EntityFrameworkMapping.Json)]
[assembly: ValueObjectDefaults(GenerateEFConverter = false, GenerateEFComparer = false)]
```

```xml
<!-- Whole project. -->
<PropertyGroup>
    <DisableValueObjectsEFGeneration>true</DisableValueObjectsEFGeneration>
</PropertyGroup>
```

## Schema and migrations

- A scalar is a **single column** of its primitive, so `dotnet ef migrations add` sees the primitive.
  Renaming the value object's property changes no schema; changing the underlying type is a column
  type change you review like any other.
- A complex-type-mapped value object is a **set of columns** named after its members; a JSON-mapped
  one is a **single JSON column**. Moving between the two is a schema change that usually needs a
  data migration to reshape existing values.
- Converters and the key generator convention are provider-independent, so one model serves SQL
  Server, PostgreSQL and SQLite; only the primitive column types differ. Keep one migration set per
  provider.
- `IDesignTimeDbContextFactory<TContext>` and model-cache keys that depend on runtime state (a query
  filter built from the current tenant, say) must be applied consistently — a cached model is reused
  for every context created the same way.

## Version support

Compiled and tested against EF Core **7, 9 and 10** (EF Core 7 needs `net8.0`), in
`src/tests/ValueObjects.EFCompatibility.IntegrationTests`, so version-specific API shifts are caught
by this build rather than by a consumer.

EF Core 8+ is required for **complex type mapping**, the complex-type block in the registry, and the
compiled-model `JsonReaderWriter` members. On EF Core 7, a complex value object reports `VO1019` and
is left unmapped — use `EFMapping = Json` or configure the property manually. Everything else
(converters, the key value generator, the convention, `ValueObjectSequentialGuid`) works on EF Core 7
and later.

## Diagnostics

`VO1009` (EF option set without an EF reference), `VO1010` (underlying type not mappable), `VO1017`
(JSON mapping without the JSON converter), `VO1018` (a complex member cannot be converted), `VO1019`
(complex type on EF Core 7 or earlier) and `VO1021` (value generation unavailable) are the EF rules.
The `purview-value-objects-diagnostics` skill carries the full `VO1xxx` catalogue.

## Where to look in the repository

- `src/src/Sample` — a runnable EF Core (SQLite) example.
- `src/tests/ValueObjects.IntegrationTests/Serialization/EntityFrameworkIntegrationTests.cs` — the
  integration tests.
- `src/tests/ValueObjects.EFCompatibility.IntegrationTests` — the per-version compatibility suite.
