using Purview.EventSourcing.Cryptography;

namespace Purview.EventSourcing.AzureKeyVault;

/// <summary>
/// An <see cref="IEventCryptoKeyStore"/> holding per-subject keys wrapped by a key in Azure Key
/// Vault.
/// </summary>
/// <remarks>
/// <para>
/// Envelope encryption: a per-subject key is generated locally and stored <em>wrapped</em> in a
/// durable store, with the wrapping key held in the vault. The vault holds one key rather than one
/// per subject, so this scales to any subject population, and a stolen copy of the durable store is
/// unreadable without the vault.
/// </para>
/// <para>
/// The trade against <see cref="AzureKeyVaultEventCryptoKeyStore"/>: erasure here is a row deleted
/// from the durable store, not a vault operation, so it does not appear in the vault's audit trail.
/// Choose the secret-per-subject store when erasure itself must be vault-audited, and this one when
/// the subject population makes that impractical.
/// </para>
/// <para>
/// <strong>Erasure is only as irreversible as the least-irreversible copy of the wrapped key.</strong>
/// Restoring the durable store from a backup brings shredded subjects back - the vault cannot
/// prevent that, because the wrapping key is still perfectly valid.
/// </para>
/// </remarks>
/// <param name="innerStore">The durable store holding wrapped keys.</param>
/// <param name="wrapping">The vault-held wrapping key.</param>
public sealed class AzureKeyVaultEnvelopeEventCryptoKeyStore(
	ISuppliedKeyEventCryptoKeyStore innerStore,
	IKeyVaultKeyWrapping wrapping
) : IEventCryptoKeyStore
{
	/// <inheritdoc/>
	public async Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		var existing = await TryGetAsync(dataSubjectId, cancellationToken);
		if (existing is not null)
			return existing;

		// Wrap before storing, so the plaintext key is never written down. The inner store's
		// get-or-add decides the winner of a concurrent create, and the value it returns is the one
		// to unwrap - not the candidate just generated, which may have lost.
		var candidate = AesGcmEventPayloadProtector.CreateKey();
		var wrapped = await wrapping.WrapAsync(candidate, cancellationToken);
		var stored = await innerStore.GetOrAddAsync(dataSubjectId, wrapped, cancellationToken);

		return await wrapping.UnwrapAsync(stored, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		var wrapped = await innerStore.TryGetAsync(dataSubjectId, cancellationToken);

		return wrapped is null ? null : await wrapping.UnwrapAsync(wrapped, cancellationToken);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Deletes the wrapped key from the durable store. The vault's wrapping key is untouched, as it
	/// protects every other subject too.
	/// </remarks>
	public Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken) =>
		innerStore.ShredAsync(dataSubjectId, cancellationToken);
}
