using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// Refuses to start when crypto-shredding is configured with a key store that cannot survive a
/// restart.
/// </summary>
/// <remarks>
/// The check runs at start-up rather than on first use, so a misconfiguration cannot reach the point
/// where events have already been written under keys that are about to be lost. By then the damage
/// is unrecoverable and silent.
/// </remarks>
/// <param name="keyStore">The configured key store.</param>
/// <param name="environment">The host environment; the check does not apply in Development.</param>
/// <param name="options">The crypto options carrying the explicit override.</param>
public sealed class NonDurableKeyStoreStartupCheck(
	IEventCryptoKeyStore keyStore,
	IHostEnvironment environment,
	IOptions<EventCryptoOptions> options
) : IHostedService
{
	/// <inheritdoc/>
	public Task StartAsync(CancellationToken cancellationToken)
	{
		if (keyStore is not INonDurableEventCryptoKeyStore)
			return Task.CompletedTask;

		if (environment.IsDevelopment() || options.Value.AllowNonDurableKeyStore)
			return Task.CompletedTask;

		throw new InvalidOperationException(
			$"Crypto-shredding is configured with '{keyStore.GetType().Name}', which does not persist keys across a restart, "
				+ $"in the '{environment.EnvironmentName}' environment. Every protected event would become permanently "
				+ "unreadable when this process restarts, which is indistinguishable from erasing every data subject. "
				+ "Register a durable key store, or set "
				+ $"'{EventCryptoOptions.Section}:{nameof(EventCryptoOptions.AllowNonDurableKeyStore)}' to true if that "
				+ "outcome is genuinely acceptable here."
		);
	}

	/// <inheritdoc/>
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
