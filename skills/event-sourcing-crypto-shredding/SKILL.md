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
builder.Services.AddSingleton<IEventCryptoKeyStore, KeyVaultEventCryptoKeyStore>();
builder.Services.AddSingleton<IEventPayloadProtector, AesGcmEventPayloadProtector>();
```

`InMemoryEventCryptoKeyStore` ships for local development and tests only: its keys die with the
process, so every payload becomes unreadable on restart. That is deliberate - a key store is the one
component where a convenient default would be actively harmful.

**The key store is the erasure mechanism.** Back it with something that has its own access control
and audit trail, and that does not silently restore deleted entries from a backup.

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
