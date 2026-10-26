using Azure;
using Azure.Security.KeyVault.Secrets;

namespace Purview.EventSourcing.AzureKeyVault;

/// <summary>
/// <see cref="IKeyVaultSecretOperations"/> over the Key Vault <see cref="SecretClient"/>.
/// </summary>
/// <remarks>
/// Deliberately thin: it translates "absent" into a null or false and otherwise lets the SDK's
/// exceptions through, so the behaviour worth testing stays in the key store.
/// </remarks>
/// <param name="client">The vault secret client.</param>
public sealed class SecretClientOperations(SecretClient client) : IKeyVaultSecretOperations
{
	/// <inheritdoc/>
	public async Task<string?> TryGetAsync(string name, CancellationToken cancellationToken)
	{
		try
		{
			var secret = await client.GetSecretAsync(name, cancellationToken: cancellationToken);

			return secret.Value.Value;
		}
		catch (RequestFailedException ex) when (ex.Status == 404)
		{
			return null;
		}
	}

	/// <inheritdoc/>
	public async Task SetAsync(
		string name,
		string value,
		IReadOnlyDictionary<string, string> tags,
		CancellationToken cancellationToken
	)
	{
		ArgumentNullException.ThrowIfNull(tags);

		KeyVaultSecret secret = new(name, value);
		foreach (var tag in tags)
			secret.Properties.Tags[tag.Key] = tag.Value;

		await client.SetSecretAsync(secret, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken)
	{
		try
		{
			var operation = await client.StartDeleteSecretAsync(name, cancellationToken);
			await operation.WaitForCompletionAsync(cancellationToken);

			return true;
		}
		catch (RequestFailedException ex) when (ex.Status == 404)
		{
			return false;
		}
	}

	/// <inheritdoc/>
	public Task PurgeAsync(string name, CancellationToken cancellationToken) =>
		client.PurgeDeletedSecretAsync(name, cancellationToken);
}
