namespace Purview.EventSourcing.AzureKeyVault;

/// <summary>
/// Wraps and unwraps per-subject keys with a key held in Azure Key Vault.
/// </summary>
/// <remarks>
/// The wrapping key never leaves the vault - the vault performs the operation - so a stolen copy of
/// the durable key store is unreadable without access to the vault. A seam rather than a direct
/// dependency on <c>CryptographyClient</c>, for the same reason as
/// <see cref="IKeyVaultSecretOperations"/>: the SDK client is not substitutable in tests.
/// </remarks>
public interface IKeyVaultKeyWrapping
{
	/// <summary>Wraps a key.</summary>
	/// <param name="key">The plaintext key.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	Task<byte[]> WrapAsync(byte[] key, CancellationToken cancellationToken);

	/// <summary>Unwraps a key.</summary>
	/// <param name="wrappedKey">The wrapped key.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	Task<byte[]> UnwrapAsync(byte[] wrappedKey, CancellationToken cancellationToken);
}
