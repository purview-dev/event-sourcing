# Generated key values

Opt a Guid-backed scalar into key generation per type, or once for the assembly:

```csharp
[Scalar(GenerateEFValueGenerator = true)]
public readonly partial record struct CustomerId
{
    public Guid Value { get; }
}

// or:
[assembly: ValueObjectDefaults(GenerateEFValueGenerator = true)]
```

The generator emits an `EF.ValueGeneratorFactory` on the value object and registers every opted-in
type in the generated `ValueObjectKeyValueGeneratorConvention`. Register that convention once:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    base.ConfigureConventions(configurationBuilder);
    configurationBuilder.UseValueObjectKeyGenerators();
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigureValueObjects();
}
```

## What the convention guarantees

- An **unset** key (still `Guid.Empty`) is assigned a time-ordered identifier when the entity is
  added, so keys are unique without a database round trip and sort by creation time in stores that
  compare identifiers byte by byte.
- A key the **domain already set** is never touched. Entity Framework Core only invokes a generator
  while the property holds its CLR default.
- The convention runs at the lowest configuration source, so an entity configuration that owns its
  keys — `ValueGeneratedNever()`, an explicit generator, a store-generated default — always wins.

The identifier is a time-ordered UUID created with base Entity Framework Core APIs only, so the
convention holds for every provider.

## Key ordering

Where a store compares the identifier's sixteen bytes in order — PostgreSQL, SQLite, MySQL, and
non-clustered SQL Server keys — version 7 ordering is what you want. SQL Server's `uniqueidentifier`
compares the **trailing six bytes first**, so plain version 7 values are effectively random there and
a clustered key fragments.

```csharp
// Default: version 7, ascending in plain byte order.
configurationBuilder.UseValueObjectKeyGenerators();

// SQL Server: the timestamp moves into the trailing six bytes, which SQL Server compares first.
configurationBuilder.UseValueObjectKeyGenerators(ValueObjectKeyOrdering.SqlServer);
```

| Ordering | Timestamp location | Ascends by creation time in | Well-formed version 7 UUID |
| --- | --- | --- | --- |
| `ValueObjectKeyOrdering.UuidV7` (default) | bytes 0-5 | PostgreSQL, SQLite, MySQL, non-clustered SQL Server keys | Yes |
| `ValueObjectKeyOrdering.SqlServer` | bytes 10-15 | SQL Server's `uniqueidentifier` ordering | No — a version 4 UUID |

Both orderings produce unique identifiers in process, keep the domain's own value when it set one,
and start from a random value so nothing leaks about the sequence.

Which bytes carry the timestamp is a **store** concern, so the ordering is chosen where the
convention is registered — in the persistence project, never on the value object.

## Minting a key in application code

`ValueObjectSequentialGuid` is public, in the `Microsoft.EntityFrameworkCore` namespace, so
application code — including code in another assembly — can mint the identifier it wants an entity to
carry, before the round trip that would otherwise assign one:

```csharp
// The same value the convention would have generated, created where the domain needs it.
var customerId = CustomerId.Create(ValueObjectSequentialGuid.NewGuid());
var customer = Customer.Create(customerId, tenantId, email, "Contoso");
```

Because Entity Framework Core only invokes a generator while the property still holds its CLR
default, the key the application set is persisted as-is.

| Member | Purpose |
| --- | --- |
| `NewGuid()` / `NewGuid(DateTimeOffset)` | Creates a version 7 identifier, from now or a given creation time. |
| `NewSqlServerGuid()` / `NewSqlServerGuid(DateTimeOffset)` | Creates a SQL Server-ordered identifier. |
| `TryGetTimestamp(Guid, out DateTimeOffset)` | Reads the creation time out of a version 7 identifier; false for any other shape. |
| `TryGetSqlServerTimestamp(Guid, out DateTimeOffset)` | Reads the creation time out of a SQL Server-ordered identifier. |
| `MinSqlServerGuidFor(DateTimeOffset)` / `MaxSqlServerGuidFor(DateTimeOffset)` | Inclusive bounds for a creation time, so `id >= Min… && id <= Max…` is an index seek. |

## Limits

- **Guid-backed scalars only**, and the Entity Framework converter must be enabled. Requesting it
  anywhere else reports `VO1021` and emits no generator.
- A value object declared in an assembly that does not reference Entity Framework Core still gets a
  generator: the consuming project emits it alongside the inline converters.
- A generated key value generator is typed as the **value object**, not its provider value, because
  Entity Framework Core assigns what a generator returns straight to the property.
