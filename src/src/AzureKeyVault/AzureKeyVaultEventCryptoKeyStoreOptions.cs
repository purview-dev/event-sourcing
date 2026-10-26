using System.ComponentModel.DataAnnotations;

namespace Purview.EventSourcing.AzureKeyVault;

/// <summary>
/// Options that configure the Azure Key Vault crypto key stores.
/// </summary>
/// <remarks>
/// Bound from the <c>EventStore:AzureKeyVault:Cryptography</c> configuration section.
/// </remarks>
public sealed class AzureKeyVaultEventCryptoKeyStoreOptions
{
	/// <summary>The configuration section name used to bind these options.</summary>
	public const string AzureKeyVaultCryptography = "EventStore:AzureKeyVault:Cryptography";

	/// <summary>The vault URI, for example <c>https://my-vault.vault.azure.net/</c>.</summary>
	[Required(AllowEmptyStrings = false)]
	public string VaultUri { get; set; } = default!;

	/// <summary>
	/// The prefix applied to every secret name, so one vault can serve several deployments.
	/// </summary>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression("^[0-9a-zA-Z-]{1,32}$")]
	public string SecretNamePrefix { get; set; } = "purview-es";

	/// <summary>
	/// Purges a secret after deleting it, so erasure is immediate rather than deferred.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A vault with soft-delete keeps a deleted secret recoverable for its retention period, so a
	/// subject is not actually erased until that window expires. Purging completes the erasure now.
	/// </para>
	/// <para>
	/// Purging fails on a vault with <em>purge protection</em> enabled, which forbids it entirely.
	/// That is a legitimate configuration - it is a safeguard against malicious deletion - but it is
	/// incompatible with erasure on request, so the store reports the conflict rather than letting a
	/// shred appear to succeed. Set this to <see langword="false"/> only if you have accepted that
	/// erasure completes when the retention window elapses.
	/// </para>
	/// </remarks>
	public bool PurgeOnShred { get; set; } = true;

	/// <summary>
	/// The name of the vault key used to wrap per-subject keys in the envelope store.
	/// </summary>
	/// <remarks>
	/// Only used by the envelope store, where the vault holds one wrapping key rather than a secret
	/// per subject. Ignored by the secret-per-subject store.
	/// </remarks>
	public string? WrappingKeyName { get; set; }
}
