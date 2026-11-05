---
name: event-sourcing-crypto-shredding
description: "Use when an event-sourced aggregate carries personal data and it must be erasable - marking events with IEventDataSubject, supplying per-subject keys through IEventCryptoKeyStore, what replay does once a key is shredded, and why snapshots are disabled while payload protection is on."
license: MIT
metadata:
  package: Purview.EventSourcing
  category: security
---

# Crypto-shredding

An event stream is append-only, so personal data cannot be deleted from it. Instead, encrypt each
subject's payloads under a key belonging to that subject and **delete the key**. The events remain;
their contents become unreadable.

## Marking an event

```csharp
[EventContract]
public sealed record CustomerDetailsChanged : IEventDataSubject
{
    public static int SchemaVersion => 1;

    public EventMetadata Metadata { get; init; }

    public string Name { get; set; } = default!;

    public string DataSubjectId { get; set; } = default!;
}
```

Only events implementing `IEventDataSubject` are encrypted. Everything else is stored as before.

`DataSubjectId` must be **stable for the subject's lifetime** - it is the key identifier, so a value
that changes orphans the earlier events. It is stored in clear alongside the ciphertext, so it must
not itself be personal data: use a surrogate identifier, never an email address.

## Supplying keys

```csharp
builder.Services.AddSqlServerEventCryptoShredding();   // or AddPostgresEventCryptoShredding()
```

One call registers the key store, the AES-GCM protector and a start-up guard. Every provider takes
an optional `IEventPayloadProtector`, so registering it turns protection on for whichever store is
configured.

| Store | Registration | Durable |
| -- | -- | -- |
| In-memory | `AddInMemoryEventCryptoShredding()` | **No** |
| SQL Server | `AddSqlServerEventCryptoShredding()` | Yes |
| PostgreSQL | `AddPostgresEventCryptoShredding()` | Yes |
| Azure Storage | `AddAzureStorageEventCryptoShredding()` | Yes |
| MongoDB | `AddMongoDBEventCryptoShredding()` | Yes |
| Azure Key Vault, secret per subject | `AddAzureKeyVaultEventCryptoShredding()` | Yes |
| Azure Key Vault, envelope | `AddAzureKeyVaultEnvelopeEventCryptoShredding<TInner>()` | Yes |

**The host refuses to start with a non-durable key store outside Development.** Keys that die with
the process make every protected payload unreadable - the same outcome as erasing every subject at
once, with no error and no way back. `EventStore:Cryptography:AllowNonDurableKeyStore` overrides it;
if you are reaching for that to make a start-up failure go away, register a durable store instead.

Writing your own store? Implement `INonDurableEventCryptoKeyStore` on any test double so the guard
recognises it, and scope keys by the current tenant - the same subject id in two tenants is two
different people.

### Choosing between the two Key Vault stores

| | Secret per subject | Envelope |
| -- | -- | -- |
| Erasure is | a vault operation, in the vault's audit trail | a row deleted from a durable store |
| Scales to | a few thousand subjects | any number |
| Vault holds | one secret per subject | one wrapping key |
| Needs a durable store | no | yes - pass it as `TInner` |

Pick secret-per-subject when erasure itself must be vault-audited; pick envelope when the subject
population makes that impractical. The envelope store never writes a usable key to the durable
store, so a stolen database is unreadable without the vault.

### Rotation, where the store supports it

SQL Server and PostgreSQL hold multiple key generations per subject. `ProtectedPayload` records
which one encrypted it, so rotating never makes existing events unreadable - old generations are
kept on purpose.

```csharp
var keyId = await rotatingStore.RotateAsync(dataSubjectId, cancellationToken);
```

Two things to keep straight:

- **Rotation is not erasure.** `ShredAsync` deletes every generation; rotation adds one. A shred
  that left an older generation behind would not be an erasure.
- **Rotation does not re-encrypt history.** It limits what a future key compromise exposes; it does
  nothing about what an attacker already has.

Elsewhere - Azure Storage, MongoDB, Key Vault, in-memory - the store does not implement
`IRotatingEventCryptoKeyStore` and payloads carry no key identifier, exactly as before.

### Caching bounds how fast an erasure takes effect

Keys are cached in process by default - an uncached store makes every protect and unprotect a round
trip, which a throttled vault will not tolerate. `ShredAsync` evicts before deleting *and again
afterwards*, because a reader racing the delete can repopulate the entry in between.

**The cache is per process**, so `KeyCacheDuration` (default 5 minutes) is the upper bound on how
long a shredded subject stays readable on another instance. That makes it a compliance parameter,
not a tuning one. Set `CacheKeys = false` where an erasure must be immediate everywhere.

If you write a decorator around a key store, implement `IEventCryptoKeyStoreDecorator` - the
start-up guard unwraps decorators to find the real store, and a decorator that hides it silently
stops the guard working.

**Erasure is only as irreversible as the least-irreversible copy of the key.** This is the thing
that fails an audit:

- A point-in-time **database restore** brings shredded keys back. Exclude the key table from
  long-retention backups, or accept that erasure completes only when the last backup ages out.
- **Key Vault purge protection** keeps a deleted secret recoverable for the retention window, so a
  "shredded" subject is not actually erased until it expires.
- **Azure Storage soft delete / point-in-time restore**, and a **MongoDB replica that has not yet
  applied the delete**, both do the same thing.

Neither is something the framework can enforce; both belong in the deployment's runbook.

## Erasing a subject

```csharp
await keyStore.ShredAsync(dataSubjectId, cancellationToken);
```

That is the whole operation. No event is rewritten or deleted, and no other subject is affected.

## What replay does afterwards

A payload whose key has been erased is surfaced as an **unknown event**:

- the aggregate version still advances,
- the read **succeeds**,
- the gap is reported through `AggregateBase.SkippedEvents`.

Replay never fails because somebody exercised their right to erasure. A caller that needs to know
whether reconstruction was complete inspects `SkippedEvents`.

```csharp
var result = await store.GetAsync<CustomerAggregate>(id, cancellationToken);
var incomplete = (result.Value as AggregateBase)?.SkippedEvents.Count > 0;
```

Design aggregates so that a skipped event does not corrupt derived state - the same tolerance an
unknown event type already requires.

## Snapshots are disabled while protection is on

A snapshot serializes aggregate state, which holds the same personal data in clear, and one snapshot
can span many subjects - so it cannot be encrypted under any single subject's key. A store
configured with an `IEventPayloadProtector` therefore **writes no snapshots at all**.

Replay from events stays correct; reads of long streams get slower. A snapshot surviving key erasure
would make the whole feature untrue, so this is not configurable.

Snapshots written *before* protection was enabled are **not** removed retrospectively. Delete them as
part of enabling protection, or the erased data survives in them.

## Where it applies

Wired for the SQL Server, PostgreSQL, MongoDB and Azure Storage event stores, including the Azure
blob route used for oversized events. The in-memory store holds live objects rather than serialized
payloads, so there is nothing to encrypt and shredding does not apply to it.

## What to verify

1. The stored payload is ciphertext - read raw storage and assert the plaintext is absent.
2. Erasure is surgical - shred one subject, assert another subject's payload still decrypts.
3. After shredding, the read succeeds and `SkippedEvents` reports the gap.
4. Assert on distinctive strings. A short fragment such as `"42"` appears in base64 by chance and
   makes the test pass or fail at random.
5. Snapshots are absent for a protected store, and any pre-existing ones are deleted.
