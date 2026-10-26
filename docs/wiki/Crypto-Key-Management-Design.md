# Crypto Key Management — Design

> **Status: mostly implemented.**
>
> Phase 1 (the start-up guard), Phase 2 (SQL Server, PostgreSQL, Azure Storage and MongoDB key
> stores) and Phase 4 (both Key Vault models) have shipped — see
> [Guarantees and Limitations](Guarantees-and-Limitations.md#key-stores). **Rotation (Phase 5) remains a proposal.** Caching
> (Phase 3) has shipped, including the evict-before-delete rule.

## The problem

Crypto-shredding is currently a trap rather than a feature.

`InMemoryEventCryptoKeyStore` holds keys in a `ConcurrentDictionary` for the lifetime of the
process. Restart the host and **every protected event becomes permanently unreadable** — and
indistinguishable from an event whose subject was deliberately erased. There is no durable
alternative in the box, and nothing stops an application registering it in production.

Its own XML docs say this is intentional ("unusable in production by construction"), and that is a
defensible position for a default. It is not defensible as the *only* option.

Two further gaps:

- **No key rotation.** `ProtectedPayload` records `alg` and `sub` but no key identifier, so there is
  no way to tell which key a payload was encrypted under. Rotation is impossible without a format
  change.
- **No caching.** Every protect/unprotect calls the store. Against an external vault that is both
  too slow and too expensive to run.

## Three tiers

The requirement is local, in-memory, and external. All three implement the existing
`IEventCryptoKeyStore`, which does not change.

| Tier | Implementation | Use |
| --- | --- | --- |
| **In-memory** | `InMemoryEventCryptoKeyStore` (exists) | Tests and local development only |
| **Local** | `SqlServerEventCryptoKeyStore`, `PostgresEventCryptoKeyStore` | Self-hosted and on-premises; keys live in a dedicated table beside the data |
| **External** | `AzureKeyVaultEventCryptoKeyStore` | Deployments where key custody must sit outside the application's database |

### The guard

A startup check that refuses `InMemoryEventCryptoKeyStore` outside Development, unless explicitly
overridden:

```csharp
services.AddEventCryptoShredding(options => options.AllowNonDurableKeyStore = true);
```

The override exists because a test host or a demo legitimately wants it; the default refuses,
because the failure mode is silent and total. It throws at startup rather than first use, so a
misconfiguration cannot reach the point where events have already been written.

## The external tier: two models

Azure Key Vault can hold the keys two ways, and they differ in what erasure means.

### Model A — one vault secret per subject

`GetOrCreateAsync` creates a secret named for the subject; `ShredAsync` deletes it.

- Erasure maps directly onto the vault's own ACL, audit trail and delete semantics.
- **Does not scale.** A vault is not designed for millions of secrets: listing is slow, throughput
  is throttled per-vault, and cost is per-operation.

### Model B — envelope encryption (recommended)

A per-subject data key (DEK) is generated locally and stored, **wrapped**, in the local tier. The
wrapping key (KEK) lives in Key Vault and never leaves it; the vault performs wrap/unwrap.

- Scales to any number of subjects; one vault object per tenant or per deployment.
- `ShredAsync` deletes the wrapped DEK row — a local delete, not a vault operation.
- Key Vault holds custody of the KEK, so a stolen database is still unreadable.

**Model B is the recommendation**, with Model A available for deployments with few subjects and a
hard requirement that erasure be a vault-audited event.

Model B means the local tier is a prerequisite for the external tier, not an alternative to it.

## The hard part: erasure is only as irreversible as the least-irreversible copy

This deserves stating plainly in the documentation, because it is the thing that makes
crypto-shredding fail an audit:

- **Key Vault soft-delete and purge protection conflict with erasure.** With purge protection
  enabled, a deleted secret is recoverable for the retention period — so a "shredded" subject is
  not actually erased until that window expires. For Model A the vault must either have purge
  protection disabled, or `ShredAsync` must delete *and purge*, and the implementation must fail
  loudly when it cannot.
- **Database backups restore keys.** For the local tier and for Model B, a point-in-time restore
  brings shredded keys back. The key table must be excluded from long-retention backups, or the
  deployment must accept that erasure completes only when the last backup containing the key ages
  out.

The existing `IEventCryptoKeyStore` remarks already warn about this. The plan is to make it
operational guidance in `docs/wiki`, not just an XML comment, and to have each implementation
document its own erasure latency.

## Caching

Against an external vault, an uncached store is unusable: every event protect/unprotect becomes a
network call.

```csharp
public sealed class CachingEventCryptoKeyStore(IEventCryptoKeyStore inner, IMemoryCache cache, IOptions<EventCryptoOptions> options)
    : IEventCryptoKeyStore;
```

The rule that makes it safe: **`ShredAsync` must evict before it deletes, and a cache miss must not
be retried from a stale replica.** A cached key outliving its erasure is a compliance failure, not a
performance quirk. Default TTL should be short (minutes) and configurable, and the cache must be
keyed by tenant as well as subject.

Decision needed: whether the cache is process-local (`IMemoryCache`) only, or whether a distributed
cache is permitted. A distributed cache holding plaintext keys is a second copy of the key material
and probably should be refused outright.

## Rotation

`ProtectedPayload` gains an optional key identifier:

```csharp
[JsonPropertyName("kid")]
public string? KeyId { get; init; }
```

Absent means "the subject's only key", which is exactly what every payload written today means — so
the change is backward compatible and needs no upcaster.

`IEventCryptoKeyStore` gains:

```csharp
Task<(string KeyId, byte[] Key)> GetOrCreateCurrentAsync(string dataSubjectId, CancellationToken cancellationToken);
Task<byte[]?> TryGetAsync(string dataSubjectId, string? keyId, CancellationToken cancellationToken);
Task<string> RotateAsync(string dataSubjectId, CancellationToken cancellationToken);
```

Rotation generates a new key and marks it current. **Old keys are retained**, because existing
events were encrypted under them — rotation limits the blast radius of a future key compromise, it
does not re-encrypt history. Rotation and erasure stay distinct: `ShredAsync` deletes *every*
generation for the subject.

This is an additive but breaking interface change for anyone who has implemented the interface.
Default implementations on the interface can keep existing implementers compiling.

## Phases

| Phase | Work |
| --- | --- |
| 1 | Startup guard refusing the in-memory store outside Development |
| 2 | SQL Server + PostgreSQL key stores, with the tenancy and monotonic-write patterns used by the subscription checkpoint stores |
| 3 | `CachingEventCryptoKeyStore`, with evict-before-delete |
| 4 | Key Vault store, Model B envelope wrapping (Model A behind an option) |
| 5 | Rotation: `kid` on the payload, the three interface methods, and an admin-triggered rotate |

Phase 1 alone removes the trap and is worth shipping on its own.

## Testing

Tenancy first, as always — a key store is per-tenant, and a key leaking across tenants is worse than
a snapshot leaking across tenants.

- One tenant cannot read, create or shred another tenant's key; the system scope can.
- `ShredAsync` makes a previously readable payload return null from `UnprotectAsync`, **through the
  cache** — the test must prime the cache first, exactly as the tenancy cache test does.
- Rotation: a payload written under key 1 stays readable after rotating to key 2; a payload written
  after rotation uses key 2; shredding removes both.
- Replay of a stream containing a shredded event yields a `SkippedEventRecord` rather than throwing.
- Key Vault tests run against a real vault or a recorded transport; the emulator story is poor, so
  this will likely be an opt-in suite with its own category.

## Decisions needed

1. **Model A or Model B** for Key Vault. (Recommendation: B, with A available.)
2. Whether a **distributed cache** may hold key material. (Recommendation: no.)
3. Whether the **key store is a separate package per provider** (`Purview.EventSourcing.SqlServer`
   gains it) or a single `Purview.EventSourcing.Cryptography.*` family.
4. Whether rotation ships in the first release or follows.
5. Whether the startup guard is **on by default** — it is a breaking change for anyone currently
   running the in-memory store in production, which is precisely the population it protects.
