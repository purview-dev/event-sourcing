# Purview.EventSourcing.AzureKeyVault

Azure Key Vault backed crypto-shredding key stores for
[Purview.EventSourcing](https://www.nuget.org/packages/Purview.EventSourcing).

Crypto-shredding encrypts an event payload under a key belonging to one data subject. Erasing that
key erases their data everywhere it appears, without rewriting or deleting a single event. The key
store is therefore the erasure mechanism, and where it lives decides who can perform an erasure and
whether it can be undone.

## Two models

```csharp
// One vault secret per data subject.
builder.Services.AddAzureKeyVaultEventCryptoShredding(options =>
{
    options.VaultUri = "https://my-vault.vault.azure.net/";
});
```

Erasure is a vault operation, inheriting the vault's own access control and audit trail. **It does
not scale to large subject populations** — a vault is throttled per vault and every read is a
network call.

```csharp
// One wrapping key in the vault; per-subject keys held wrapped in a durable store.
builder.Services.AddSqlServerEventCryptoShredding();
builder.Services.AddAzureKeyVaultEnvelopeEventCryptoShredding(options =>
{
    options.VaultUri = "https://my-vault.vault.azure.net/";
    options.WrappingKeyName = "event-sourcing-kek";
});
```

Scales to any number of subjects, and a stolen database is still unreadable without the vault.
Erasure deletes the wrapped key from the durable store rather than calling the vault.

## Purge protection is incompatible with erasure on request

A vault with soft-delete keeps a deleted secret recoverable for its retention period, so a subject
is not actually erased until that window expires. The secret-per-subject store therefore purges
after deleting, by default.

**Purge protection forbids purging outright.** It is a reasonable safeguard against malicious
deletion, and it is incompatible with erasing on request. Rather than let a shred appear to succeed,
the store raises an error explaining the conflict. Either disable purge protection on the vault that
holds these secrets, or set `PurgeOnShred` to `false` and accept that erasure completes when the
retention window elapses.

## Erasure is only as irreversible as the least-irreversible copy

The same caveat applies to the envelope model, where the wrapped keys live in a database: a
point-in-time restore brings them back. Exclude the key table from long-retention backups, or accept
the delay. This is an operational control the framework cannot enforce.

See `docs/wiki/Crypto-Key-Management-Design.md` and the `event-sourcing-crypto-shredding` skill.
