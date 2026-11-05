using Azure;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.AzureKeyVault;

/// <summary>
/// An <see cref="IEventCryptoKeyStore"/> holding one Key Vault secret per data subject.
/// </summary>
/// <remarks>
/// <para>
/// Erasure maps directly onto the vault: deleting the secret is the irreversible act, and it
/// inherits the vault's own access control and audit trail. That makes this the right choice when
/// erasure itself must be a vault-audited event.
/// </para>
/// <para>
/// <strong>It does not scale to large subject populations.</strong> A vault is not designed to hold
/// millions of secrets: throughput is throttled per vault, and every read is a network call. For a
/// large population use <see cref="AzureKeyVaultEnvelopeEventCryptoKeyStore"/>, which keeps one
/// wrapping key in the vault instead.
/// </para>
/// <para>
/// Keys are scoped to the current tenant: the same data-subject identifier in two tenants is two
/// different people, and the tenant is part of the derived secret name.
/// </para>
/// </remarks>
public sealed class AzureKeyVaultEventCryptoKeyStore : IEventCryptoKeyStore
{
	readonly IKeyVaultSecretOperations _secrets;
	readonly AzureKeyVaultEventCryptoKeyStoreOptions _options;
	readonly ICurrentTenant _currentTenant;

	/// <summary>Creates the store.</summary>
	/// <param name="secrets">The vault secret operations.</param>
	/// <param name="options">The key-store options.</param>
	/// <param name="currentTenant">The tenant scope keys are read and written under.</param>
	public AzureKeyVaultEventCryptoKeyStore(
		IKeyVaultSecretOperations secrets,
		IOptions<AzureKeyVaultEventCryptoKeyStoreOptions> options,
		ICurrentTenant? currentTenant = null
	)
	{
		ArgumentNullException.ThrowIfNull(secrets);
		ArgumentNullException.ThrowIfNull(options);

		_secrets = secrets;
		_options = options.Value;
		_currentTenant = currentTenant ?? new SystemCurrentTenant();
	}

	/// <inheritdoc/>
	public async Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		var existing = await TryGetAsync(dataSubjectId, cancellationToken);
		if (existing is not null)
			return existing;

		var key = AesGcmEventPayloadProtector.CreateKey();
		Dictionary<string, string> tags = new(StringComparer.Ordinal)
		{
			[KeyVaultSecretNaming.TenantTag] = TenantColumn.Encode(_currentTenant.Current),
			[KeyVaultSecretNaming.DataSubjectTag] = dataSubjectId,
		};

		await _secrets.SetAsync(NameFor(dataSubjectId), Convert.ToBase64String(key), tags, cancellationToken);

		// Re-read rather than trusting the local value: a concurrent caller may have set a different
		// one, and both must end up with whichever the vault now holds, or payloads written by the
		// loser become unreadable.
		return await TryGetAsync(dataSubjectId, cancellationToken)
			?? throw new InvalidOperationException(
				$"The key for data subject '{dataSubjectId}' could not be read back immediately after being created."
			);
	}

	/// <inheritdoc/>
	public async Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		// A missing secret means never created, or erased. Both are expected during replay - erasure
		// is what this looks like from the inside - so it is a null rather than a throw.
		var value = await _secrets.TryGetAsync(NameFor(dataSubjectId), cancellationToken);

		return value is null ? null : Convert.FromBase64String(value);
	}

	/// <inheritdoc/>
	public async Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		var name = NameFor(dataSubjectId);

		if (!await _secrets.DeleteAsync(name, cancellationToken))
			return false;

		if (!_options.PurgeOnShred)
			return true;

		// Soft-delete alone leaves the secret recoverable for the vault's retention period, so the
		// subject is not actually erased yet. Purging completes it.
		try
		{
			await _secrets.PurgeAsync(name, cancellationToken);
		}
		catch (RequestFailedException ex) when (ex.Status is 403 or 400)
		{
			// Purge protection forbids purging outright. Reporting success here would be a lie: the
			// key is still recoverable, so the subject is not erased.
			throw new InvalidOperationException(
				$"The key for data subject '{dataSubjectId}' was soft-deleted but could not be purged, so it remains "
					+ "recoverable for the vault's retention period and the subject is not yet erased. This vault has purge "
					+ "protection enabled, which is incompatible with erasure on request. Either disable purge protection, "
					+ $"or set '{AzureKeyVaultEventCryptoKeyStoreOptions.AzureKeyVaultCryptography}:"
					+ $"{nameof(AzureKeyVaultEventCryptoKeyStoreOptions.PurgeOnShred)}' to false and accept that erasure "
					+ "completes when the retention window elapses.",
				ex
			);
		}

		return true;
	}

	string NameFor(string dataSubjectId) =>
		KeyVaultSecretNaming.NameFor(
			_options.SecretNamePrefix,
			TenantColumn.Encode(_currentTenant.Current),
			dataSubjectId
		);
}
