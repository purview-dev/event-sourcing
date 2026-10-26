using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// The start-up guard that refuses a key store which cannot survive a restart.
/// </summary>
/// <remarks>
/// Losing the keys is indistinguishable from erasing every data subject, and nothing reports that it
/// happened - so the only safe moment to object is before the host starts, and before any event has
/// been written under a key that is about to be lost.
/// </remarks>
public sealed class NonDurableKeyStoreStartupCheckTests
{
	[Test]
	public async Task StartAsync_GivenANonDurableStoreInProduction_Throws()
	{
		var check = CreateCheck(new InMemoryEventCryptoKeyStore(), "Production");

		await Assert
			.That(async () => await check.StartAsync(CancellationToken.None))
			.Throws<InvalidOperationException>();
	}

	[Test]
	public async Task StartAsync_GivenANonDurableStoreInDevelopment_DoesNotThrow()
	{
		var check = CreateCheck(new InMemoryEventCryptoKeyStore(), "Development");

		await Assert.That(async () => await check.StartAsync(CancellationToken.None)).ThrowsNothing();
	}

	[Test]
	public async Task StartAsync_GivenANonDurableStoreAndTheExplicitOverride_DoesNotThrow()
	{
		var check = CreateCheck(new InMemoryEventCryptoKeyStore(), "Production", allowNonDurable: true);

		await Assert.That(async () => await check.StartAsync(CancellationToken.None)).ThrowsNothing();
	}

	[Test]
	public async Task StartAsync_GivenADurableStoreInProduction_DoesNotThrow()
	{
		var check = CreateCheck(new DurableKeyStoreStub(), "Production");

		await Assert.That(async () => await check.StartAsync(CancellationToken.None)).ThrowsNothing();
	}

	// The message is what an operator acts on at 3am, so it has to name the store, the environment
	// and the way out.
	[Test]
	public async Task StartAsync_WhenItRefuses_ExplainsWhatToDo()
	{
		var check = CreateCheck(new InMemoryEventCryptoKeyStore(), "Production");

		var exception = await Assert
			.That(async () => await check.StartAsync(CancellationToken.None))
			.Throws<InvalidOperationException>();

		await Assert.That(exception!.Message).Contains(nameof(InMemoryEventCryptoKeyStore));
		await Assert.That(exception.Message).Contains("Production");
		await Assert.That(exception.Message).Contains(nameof(EventCryptoOptions.AllowNonDurableKeyStore));
	}

	// The guard is only useful if it is wired by the registration everyone uses.
	[Test]
	public async Task AddInMemoryEventCryptoShredding_RegistersTheGuardAndTheProtector()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddSingleton<IHostEnvironment>(new HostEnvironmentStub("Production"));
		services.AddEventSourcing().AddInMemoryEventCryptoShredding();

		await using var provider = services.BuildServiceProvider();

		await Assert.That(provider.GetService<IEventPayloadProtector>()).IsNotNull();
		await Assert.That(provider.GetServices<IHostedService>().OfType<NonDurableKeyStoreStartupCheck>()).IsNotEmpty();
	}

	static NonDurableKeyStoreStartupCheck CreateCheck(
		IEventCryptoKeyStore keyStore,
		string environmentName,
		bool allowNonDurable = false
	) =>
		new(
			keyStore,
			new HostEnvironmentStub(environmentName),
			Options.Create(new EventCryptoOptions { AllowNonDurableKeyStore = allowNonDurable })
		);

	sealed class HostEnvironmentStub(string environmentName) : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = environmentName;

		public string ApplicationName { get; set; } = "Tests";

		public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

		public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
			new Microsoft.Extensions.FileProviders.NullFileProvider();
	}

	// Durable by omission: it does not carry the non-durable marker.
	sealed class DurableKeyStoreStub : IEventCryptoKeyStore
	{
		public Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			Task.FromResult(AesGcmEventPayloadProtector.CreateKey());

		public Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			Task.FromResult<byte[]?>(null);

		public Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			Task.FromResult(false);
	}
}
