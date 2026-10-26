using System.ComponentModel;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.AzureKeyVault;
using Purview.EventSourcing.Cryptography;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Azure Key Vault crypto key stores.
/// </summary>
/// <remarks>
/// The members of this type are hidden from IntelliSense as the type is only intended to be consumed
/// through the <see langword="static"/> using for the <c>Microsoft.Extensions.DependencyInjection</c> namespace.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		/// Registers crypto-shredding with one Key Vault secret per data subject.
		/// </summary>
		/// <param name = "configure">Optional <see cref = "AzureKeyVaultEventCryptoKeyStoreOptions"/> configuration.</param>
		/// <param name = "credential">The credential used to reach the vault; defaults to <see cref = "DefaultAzureCredential"/>.</param>
		/// <returns>The <paramref name = "services"/> for chaining.</returns>
		/// <remarks>
		/// <para>
		/// Erasure is a vault operation, so it inherits the vault's access control and audit trail.
		/// That is the reason to choose this over the envelope store.
		/// </para>
		/// <para>
		/// <strong>It does not scale to large subject populations</strong> - a vault is throttled per
		/// vault and holds one secret per subject. Prefer
		/// <c>AddAzureKeyVaultEnvelopeEventCryptoShredding</c> beyond a few thousand subjects.
		/// </para>
		/// </remarks>
		public IServiceCollection AddAzureKeyVaultEventCryptoShredding(
			Action<AzureKeyVaultEventCryptoKeyStoreOptions>? configure = null,
			TokenCredential? credential = null
		)
		{
			services.AddAzureKeyVaultCryptoOptions(configure);

			services.TryAddSingleton(provider =>
			{
				var options = provider.GetRequiredService<IOptions<AzureKeyVaultEventCryptoKeyStoreOptions>>().Value;

				return new SecretClient(new Uri(options.VaultUri), credential ?? new DefaultAzureCredential());
			});
			services.TryAddSingleton<IKeyVaultSecretOperations, SecretClientOperations>();

			services.AddEventCryptoShredding<AzureKeyVaultEventCryptoKeyStore>();

			return services;
		}

		/// <summary>
		/// Registers crypto-shredding with per-subject keys wrapped by a Key Vault key and held in a
		/// durable store.
		/// </summary>
		/// <typeparam name = "TInnerStore">The durable store holding wrapped keys.</typeparam>
		/// <param name = "configure">Optional <see cref = "AzureKeyVaultEventCryptoKeyStoreOptions"/> configuration.</param>
		/// <param name = "credential">The credential used to reach the vault; defaults to <see cref = "DefaultAzureCredential"/>.</param>
		/// <returns>The <paramref name = "services"/> for chaining.</returns>
		/// <remarks>
		/// <para>
		/// The vault holds one wrapping key rather than one secret per subject, so this scales to any
		/// subject population, and a stolen copy of the durable store is unreadable without the vault.
		/// </para>
		/// <para>
		/// Erasure deletes the wrapped key from the durable store, so it does **not** appear in the
		/// vault's audit trail. Use <c>AddAzureKeyVaultEventCryptoShredding</c> when erasure itself must
		/// be vault-audited.
		/// </para>
		/// </remarks>
		public IServiceCollection AddAzureKeyVaultEnvelopeEventCryptoShredding<TInnerStore>(
			Action<AzureKeyVaultEventCryptoKeyStoreOptions>? configure = null,
			TokenCredential? credential = null
		)
			where TInnerStore : class, ISuppliedKeyEventCryptoKeyStore
		{
			services.AddAzureKeyVaultCryptoOptions(configure);

			services.TryAddSingleton<ISuppliedKeyEventCryptoKeyStore, TInnerStore>();

			services.TryAddSingleton<IKeyVaultKeyWrapping>(provider =>
			{
				var options = provider.GetRequiredService<IOptions<AzureKeyVaultEventCryptoKeyStoreOptions>>().Value;
				if (string.IsNullOrWhiteSpace(options.WrappingKeyName))
				{
					throw new InvalidOperationException(
						$"Envelope crypto-shredding requires a wrapping key. Configure '{AzureKeyVaultEventCryptoKeyStoreOptions.AzureKeyVaultCryptography}:"
							+ $"{nameof(AzureKeyVaultEventCryptoKeyStoreOptions.WrappingKeyName)}'."
					);
				}

				KeyClient keyClient = new(new Uri(options.VaultUri), credential ?? new DefaultAzureCredential());
				var key = keyClient.GetKey(options.WrappingKeyName);

				return new CryptographyClientKeyWrapping(
					new CryptographyClient(key.Value.Id, credential ?? new DefaultAzureCredential())
				);
			});

			services.AddEventCryptoShredding<AzureKeyVaultEnvelopeEventCryptoKeyStore>();

			return services;
		}

		/// <summary>
		/// Binds and validates the Key Vault key-store options.
		/// </summary>
		/// <param name = "configure">Optional configuration callback.</param>
		/// <returns>The <paramref name = "services"/> for chaining.</returns>
		internal IServiceCollection AddAzureKeyVaultCryptoOptions(
			Action<AzureKeyVaultEventCryptoKeyStoreOptions>? configure
		)
		{
			services
				.AddOptions<AzureKeyVaultEventCryptoKeyStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
					{
						configuration
							.GetSection(AzureKeyVaultEventCryptoKeyStoreOptions.AzureKeyVaultCryptography)
							.Bind(options);
						configure?.Invoke(options);
					}
				);

			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<AzureKeyVaultEventCryptoKeyStoreOptions>,
					AzureKeyVaultEventCryptoKeyStoreOptionsValidator
				>()
			);

			return services;
		}
	}
}
