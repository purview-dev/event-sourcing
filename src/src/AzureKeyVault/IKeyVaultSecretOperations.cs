using Azure;

namespace Purview.EventSourcing.AzureKeyVault;

/// <summary>
/// The vault secret operations the key store depends on.
/// </summary>
/// <remarks>
/// A seam over <c>SecretClient</c>, whose public methods are not all virtual and so cannot be
/// substituted directly. Keeping it narrow puts the decisions worth testing - tenant scoping, what
/// a missing secret means, and whether a shred actually erased anything - in the store rather than
/// behind an SDK call.
/// </remarks>
public interface IKeyVaultSecretOperations
{
	/// <summary>Gets a secret's value, or <see langword="null"/> when it does not exist.</summary>
	/// <param name="name">The secret name.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	Task<string?> TryGetAsync(string name, CancellationToken cancellationToken);

	/// <summary>Creates or replaces a secret.</summary>
	/// <param name="name">The secret name.</param>
	/// <param name="value">The secret value.</param>
	/// <param name="tags">Tags recorded alongside the secret.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	Task SetAsync(
		string name,
		string value,
		IReadOnlyDictionary<string, string> tags,
		CancellationToken cancellationToken
	);

	/// <summary>
	/// Deletes a secret, returning <see langword="false"/> when there was nothing to delete.
	/// </summary>
	/// <param name="name">The secret name.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <remarks>
	/// On a soft-delete vault this leaves the secret recoverable for the retention period, so it is
	/// not on its own an erasure.
	/// </remarks>
	Task<bool> DeleteAsync(string name, CancellationToken cancellationToken);

	/// <summary>
	/// Purges a soft-deleted secret, completing the erasure.
	/// </summary>
	/// <param name="name">The secret name.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <exception cref="RequestFailedException">
	/// Thrown with status 403 or 400 when the vault has purge protection enabled, which forbids
	/// purging outright.
	/// </exception>
	Task PurgeAsync(string name, CancellationToken cancellationToken);
}
