---
name: purview-value-objects
description: >-
  Use when modelling domain values with Purview.ValueObjects - scalar, complex or contextual value
  objects, typed identifiers, the Create/Hydrate split, OnNormalize/OnValidate hooks, built-in string
  normalization, assembly-level ValueObjectDefaults, JSON deserialization modes, or deciding where an
  invariant belongs.
metadata:
  product: Purview.ValueObjects
  repository: purview-dev/value-objects
---

# Purview value objects

`Purview.ValueObjects` source-generates the plumbing for value objects: factories, equality,
comparison, formatting, parsing and JSON. You declare the shape and the invariants; the generator
emits the rest.

Three shapes, and the choice is forced by what the value *is*:

| Attribute | Use for | Declaration |
| --- | --- | --- |
| `[Scalar]` / `[Scalar<T>]` | one primitive with a name and rules (ids, codes, emails) | `readonly partial record struct` |
| `[ValueObject]` | a small fixed group of members that travel together | `partial record` / `readonly partial record struct` |
| `IContextualValueObject<TSelf, TValue, TOwner>` | a value whose validity depends on the owner's current state | a `[Scalar]` that also implements the interface |

All three must be **top-level** and **partial**, and none may be generic. Nesting is `VO1002`,
generics are `VO1003`, a missing `partial` is `VO1001`. The `purview-value-objects-diagnostics`
skill carries the full rule list.

## The `Create` / `Hydrate` split

This is the single most important thing to get right. Every value object gets both:

- **`Create(value)`** — the strict path. Runs `OnNormalize`, then `OnValidate`, then constructs.
  Use it for command input, user input, parsing, and anything crossing a trust boundary.
- **`Hydrate(value)`** — the reconstruction path. Constructs without validating. Use it for
  persisted state, event replay and deserialization, where validation already happened and
  re-running it would reject historical data that was legal when it was written.
- **`TryCreate(value, out result)`** — `Create` without the throw; returns `false` on failure.

| Path | Behaviour |
| --- | --- |
| `Create(...)` | Throws — the hook's exception, or `ZodException` when a ZodSharp schema is generated for the type. |
| `TryCreate(...)` | Returns `false` instead of throwing. |
| `Hydrate(...)` | Never validates, never normalizes. |

Never make `Hydrate` validate "for safety". Replay paths exist precisely because yesterday's rules
are not today's.

## Scalar value objects

```csharp
[Scalar]
public readonly partial record struct EmailAddress
{
    public string Value { get; }

    static partial void OnNormalize(ref string value) => value = value?.Trim().ToLowerInvariant()!;

    static partial void OnValidate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Email is required.", nameof(value));

        if (!value.Contains('@', StringComparison.Ordinal))
            throw new ArgumentException("Invalid email format.", nameof(value));
    }
}
```

- Declare the scalar property yourself (`[Scalar]`), or let the generator own it (`[Scalar<string>]`).
  With `[Scalar<T>]` you must **not** declare a member of that name — that is `VO1022`.
- The default property name is `Value`; `ScalarAttribute.PropertyName` changes it. A `[Scalar]` type
  with no matching property is `VO1004`.
- Prefer `readonly record struct`. Anything else is `VO1006`, because equality, immutability and
  allocation behaviour stop matching the contract.
- You do not need to declare the private constructor. The generator emits one.

### Built-in string normalization

Trim and casing are common enough to configure rather than hand-write:

```csharp
[Scalar<string>(Trim = true, Casing = StringCasing.LowerInvariant)]
public readonly partial record struct EmailAddress { }

[ValueObject]
public readonly partial record struct Contact
{
    [StringNormalize(Trim = true)]
    public string Email { get; init; }

    public string Name { get; init; }
}
```

Trim runs first, then casing, then validation — and only inside the generated `Create`. Casing is
invariant-culture only, deliberately: a canonical form that depends on the ambient thread culture is
not canonical. A hand-written `OnNormalize` or `Create` wins and the option is ignored (`VO1023`);
the option on a non-string is ignored (`VO1024`); restating a value the assembly default already sets
is reported as redundant (`VO1026`/`VO1027`). `[StringNormalize]` itself only belongs on a complex value
object member — on a `[Scalar]` or any other type it is the error `VO1025`, and MSBuild can supply the
assembly defaults with `ValueObjectsDefaultTrim` / `ValueObjectsDefaultCasing`.

## Complex value objects

```csharp
[ValueObject]
public sealed partial record UserDetails(Guid Id, string? DisplayName, bool IsActive = true)
{
    static partial void OnNormalize(ref Guid id, ref string? displayName, ref bool isActive)
    {
        if (!isActive)
            displayName = null;
    }

    partial void OnValidate(Guid id, string? displayName, bool isActive)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Id must be a valid GUID.", nameof(id));
    }
}
```

Normalize every component together, before validation, so cross-member canonicalization is possible.
Keep members `readonly`/`init`-only — a mutable member is `VO1016` and makes equality unstable. On a
`struct`, declare `OnValidate` as `readonly` when it does not mutate; the generator mirrors the
modifier.

## Contextual value objects

When validity depends on the owning instance's current state — a state machine, a cross-field rule
that needs the aggregate — implement `IContextualValueObject<TSelf, TValue, TOwner>`:

```csharp
[Scalar]
public readonly partial record struct OrderStatus
    : IContextualValueObject<OrderStatus, OrderStatusCode, Order>
{
    public OrderStatusCode Value { get; }

    public static OrderStatus Create(OrderStatusCode value, in ValueObjectContext<Order> context)
    {
        var current = context.Owner.Status.Value;

        return IsValidTransition(current, value)
            ? new(value)
            : throw new InvalidOperationException($"Invalid transition {current} -> {value}.");
    }
}
```

`ValueObjectContext<TOwner>` carries `Owner`, `MemberName` and an optional `Reason`. Keep the
contextual `Create` deterministic and side-effect free, and keep plain `Hydrate` available so replay
does not fail on historical transitions.

## Where an invariant belongs

| Concern | Lives in |
| --- | --- |
| Primitive format, canonicalization | `OnNormalize` / `OnValidate` on the value object |
| Cross-field invariants within the value | `partial void OnValidate(...)` on a `[ValueObject]` |
| Owner lifecycle / state-machine transitions | contextual `Create(TValue, in ValueObjectContext<TOwner>)` |
| Schema rules, DTO validation, API error shapes | ZodSharp — the `purview-value-objects-zodsharp` skill |
| Persistence and query shape | the entity mapping — the `purview-value-objects-efcore` skill |

Validation must be pure: deterministic, no I/O, no clock, no ambient culture.

## Standard interfaces

A scalar mirrors the standard interfaces its underlying type actually implements, so it behaves like
that type in generic code:

| Interface | Mirrored when | Forwarded member |
| --- | --- | --- |
| `IEquatable<TValue>` | the underlying type implements it | `Equals(TValue)` |
| `IFormattable`, `ToString(string?)` | the underlying type implements/declares them | `ToString(format[, provider])` |
| `ISpanFormattable` / `IUtf8SpanFormattable` | the underlying type implements them | `TryFormat` |
| `IParsable<T>` / `ISpanParsable<T>` / `IUtf8SpanParsable<T>` | the underlying type implements them | `Parse` / `TryParse` |

Detection is per type **and** per target framework, so `string` gets parsing but not span formatting
and an enum gets the reverse. Declaring one of an interface's members yourself skips that interface.

`Parse` goes through `Create` (so it normalizes and validates); `TryParse` goes through `TryCreate`.
`IComparable<TSelf>` and `IComparable` are always implemented, and a scalar always exposes
`CompareTo(TValue)`, because the contract requires it. Numeric/arithmetic interfaces, `IConvertible`
and collection interfaces are deliberately **not** mirrored.

## JSON and deserialization modes

Both attributes default to `ValueObjectDeserializationMode.Hydrate`, so a JSON read does not
re-validate. Choose `Strict` only when round-trip fidelity genuinely requires re-running validation
on read — and then make sure a `Create` overload exists to re-validate through, or you get `VO1007`.

## Assembly-level defaults

Repeat nothing per type that the assembly can say once:

```csharp
[assembly: ValueObjectDefaults(Trim = true, Casing = StringCasing.LowerInvariant)]
```

Per-type and per-member settings override it.

## Value objects in a domain model

Value objects carry identity and invariants; entities and aggregates carry lifecycle. Keep the
dependency graph clean by splitting them:

| Project | References | Contains |
| --- | --- | --- |
| Domain (`MyApp.Core`) | `Purview.ValueObjects`, optionally `Purview.ZodSharp` | Value objects, entities/aggregates, domain services. **No Entity Framework.** |
| Persistence (`MyApp.Persistence`) | EF Core, the domain project | `DbContext`, entity configuration, `ConfigureValueObjects()`, migrations |

A domain project with no EF reference gets no EF members generated into it. That is the point: the
*consuming* project discovers the value objects from their attributes and generates the converters
into the persistence assembly, so no EF code leaks into the domain.

**Typed identifiers.** Make every identity a scalar value object, so a `CustomerId` can never be
passed where an `OrderId` is expected:

```csharp
// Domain project - no Entity Framework reference.
[Scalar]
public readonly partial record struct CustomerId
{
    public Guid Value { get; }
}

public sealed class Customer
{
    Customer(CustomerId id, EmailAddress email) => (Id, Email) = (id, email);

    public CustomerId Id { get; }

    public EmailAddress Email { get; private set; }

    public static Customer Create(CustomerId id, EmailAddress email) => new(id, email);
}
```

Entities beside them are ordinary classes: private constructor, static `Create` factory that
validates, members typed as value objects rather than primitives. The factory is the command
boundary; mutation methods enforce invariants spanning members.

## Queryability

Primitive-backed scalars are the most query-friendly shape — a provider compares the value object
directly. A scalar wrapping a complex CLR type keeps its invariants and serialization, but a deep
predicate through `.Value` may not translate to SQL. Do not assume
`aggregate.ComplexScalar.Value.Child.Prop` is translatable; if you need to filter on it, expose a
separately mapped mirror property derived from canonical state, and test the exact predicate.

## Checklist for a new value object

1. Shape: scalar, complex, or contextual.
2. Normalization rules per member (hand-written hook, or configured trim/casing).
3. Validation rules and the exception each one throws.
4. Whether anything constructs it from storage — if so, it uses `Hydrate`.
5. Deserialization mode, if not the default.
6. Persistence shape, if it is stored.
7. Queryability, if it is filtered on.
