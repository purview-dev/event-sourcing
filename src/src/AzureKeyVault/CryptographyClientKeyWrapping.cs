using Azure.Security.KeyVault.Keys.Cryptography;

namespace Purview.EventSourcing.AzureKeyVault;

/// <summary>
/// <see cref="IKeyVaultKeyWrapping"/> over the Key Vault <see cref="CryptographyClient"/>.
/// </summary>
/// <remarks>
/// Uses RSA-OAEP with SHA-256, which a standard vault key supports. The algorithm is recorded
/// nowhere in the stored bytes, so changing it makes existing wrapped keys unreadable - treat it as
/// part of the persisted format rather than a tunable.
/// </remarks>
/// <param name="client">The vault cryptography client, bound to the wrapping key.</param>
public sealed class CryptographyClientKeyWrapping(CryptographyClient client) : IKeyVaultKeyWrapping
{
	/// <inheritdoc/>
	public async Task<byte[]> WrapAsync(byte[] key, CancellationToken cancellationToken)
	{
		var result = await client.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, key, cancellationToken);

		return result.EncryptedKey;
	}

	/// <inheritdoc/>
	public async Task<byte[]> UnwrapAsync(byte[] wrappedKey, CancellationToken cancellationToken)
	{
		var result = await client.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrappedKey, cancellationToken);

		return result.Key;
	}
}
