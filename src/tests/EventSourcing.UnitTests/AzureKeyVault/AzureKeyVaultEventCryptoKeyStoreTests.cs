using System.Collections.Concurrent;
using Azure;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.AzureKeyVault;

/// <summary>
/// The secret-per-subject Key Vault key store, against a stubbed vault.
/// </summary>
/// <remarks>
/// Key Vault has no emulator, so the vault is stubbed through
/// <see cref="IKeyVaultSecretOperations"/>. That covers what is worth asserting here: tenant
/// scoping, the missing-secret-means-erased contract, and the purge behaviour that decides whether
/// an erasure is real or merely deferred.
/// </remarks>
public sealed class AzureKeyVaultEventCryptoKeyStoreTests
{
	[Test]
	public async Task TryGetAsync_GivenAnotherTenantsSubject_DoesNotReturnTheKey()
	{
		var store = CreateStore(new VaultStub());

		await AsTenantAsync("acme", () => store.GetOrCreateAsync("subject-1", CancellationToken.None));
		var other = await AsTenantAsync("globex", () => store.TryGetAsync("subject-1", CancellationToken.None));

		await Assert.That(other).IsNull();
	}

	[Test]
	public async Task GetOrCreateAsync_GivenTheSameSubjectInTwoTenants_CreatesDifferentKeys()
	{
		var store = CreateStore(new VaultStub());

		var acme = await AsTenantAsync("acme", () => store.GetOrCreateAsync("subject-1", CancellationToken.None));
		var globex = await AsTenantAsync("globex", () => store.GetOrCreateAsync("subject-1", CancellationToken.None));

		await Assert.That(acme).IsNotEquivalentTo(globex);
	}

	[Test]
	public async Task ShredAsync_GivenAnotherTenantsSubject_LeavesTheOwnersKeyIntact()
	{
		var store = CreateStore(new VaultStub());

		var owner = await AsTenantAsync("acme", () => store.GetOrCreateAsync("s", CancellationToken.None));
		var shredded = await AsTenantAsync("globex", () => store.ShredAsync("s", CancellationToken.None));
		var stillThere = await AsTenantAsync("acme", () => store.TryGetAsync("s", CancellationToken.None));

		await Assert.That(shredded).IsFalse();
		await Assert.That(stillThere).IsEquivalentTo(owner);
	}

	[Test]
	public async Task GetOrCreateAsync_CalledTwice_ReturnsTheSameKey()
	{
		var store = CreateStore(new VaultStub());

		var first = await AsTenantAsync("acme", () => store.GetOrCreateAsync("s", CancellationToken.None));
		var second = await AsTenantAsync("acme", () => store.GetOrCreateAsync("s", CancellationToken.None));

		await Assert.That(first).IsEquivalentTo(second);
	}

	// A missing secret is what erasure looks like from the inside, so it must not throw.
	[Test]
	public async Task TryGetAsync_GivenNoSecret_ReturnsNull()
	{
		var store = CreateStore(new VaultStub());

		var key = await AsTenantAsync("acme", () => store.TryGetAsync("never-created", CancellationToken.None));

		await Assert.That(key).IsNull();
	}

	[Test]
	public async Task ShredAsync_GivenAnExistingKey_DeletesAndPurgesIt()
	{
		VaultStub vault = new();
		var store = CreateStore(vault);

		await AsTenantAsync("acme", () => store.GetOrCreateAsync("s", CancellationToken.None));
		var shredded = await AsTenantAsync("acme", () => store.ShredAsync("s", CancellationToken.None));
		var afterwards = await AsTenantAsync("acme", () => store.TryGetAsync("s", CancellationToken.None));

		await Assert.That(shredded).IsTrue();
		await Assert.That(afterwards).IsNull();
		await Assert.That(vault.Purged).IsNotEmpty();
	}

	[Test]
	public async Task ShredAsync_GivenNoKey_ReportsThatNothingWasErased()
	{
		var store = CreateStore(new VaultStub());

		var shredded = await AsTenantAsync("acme", () => store.ShredAsync("absent", CancellationToken.None));

		await Assert.That(shredded).IsFalse();
	}

	// Soft-delete without purge leaves the key recoverable, so the subject is not erased. Reporting
	// success would be a lie, so the store raises instead.
	[Test]
	public async Task ShredAsync_WhenPurgeIsForbidden_RefusesToReportSuccess()
	{
		var store = CreateStore(new VaultStub { PurgeForbidden = true });

		await AsTenantAsync("acme", () => store.GetOrCreateAsync("s", CancellationToken.None));

		var exception = await Assert
			.That(async () => await AsTenantAsync("acme", () => store.ShredAsync("s", CancellationToken.None)))
			.Throws<InvalidOperationException>();

		await Assert.That(exception!.Message).Contains("purge protection");
		await Assert.That(exception.Message).Contains(nameof(AzureKeyVaultEventCryptoKeyStoreOptions.PurgeOnShred));
	}

	// Opting out is allowed, but only deliberately: the key stays recoverable until the retention
	// window elapses.
	[Test]
	public async Task ShredAsync_WithPurgeDisabled_SoftDeletesWithoutPurging()
	{
		VaultStub vault = new() { PurgeForbidden = true };
		var store = CreateStore(vault, purgeOnShred: false);

		await AsTenantAsync("acme", () => store.GetOrCreateAsync("s", CancellationToken.None));
		var shredded = await AsTenantAsync("acme", () => store.ShredAsync("s", CancellationToken.None));

		await Assert.That(shredded).IsTrue();
		await Assert.That(vault.Purged).IsEmpty();
	}

	// The subject identifier must not appear in the secret name: a vault name shows up in portal
	// listings and diagnostics that are visible far more widely than the key itself.
	[Test]
	public async Task SecretNames_DoNotContainTheSubjectOrTenant()
	{
		VaultStub vault = new();
		var store = CreateStore(vault);

		await AsTenantAsync("acme", () => store.GetOrCreateAsync("subject-1", CancellationToken.None));

		await Assert.That(vault.Names.Single()).DoesNotContain("subject-1");
		await Assert.That(vault.Names.Single()).DoesNotContain("acme");
	}

	static async Task<T> AsTenantAsync<T>(string tenant, Func<Task<T>> operation)
	{
		using (AmbientCurrentTenant.BeginScope(TenantScope.For(tenant)))
			return await operation();
	}

	static AzureKeyVaultEventCryptoKeyStore CreateStore(IKeyVaultSecretOperations vault, bool purgeOnShred = true) =>
		new(
			vault,
			Options.Create(
				new AzureKeyVaultEventCryptoKeyStoreOptions
				{
					VaultUri = "https://stub.vault.azure.net/",
					PurgeOnShred = purgeOnShred,
				}
			),
			new AmbientCurrentTenant()
		);

	sealed class VaultStub : IKeyVaultSecretOperations
	{
		readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

		public bool PurgeForbidden { get; init; }

		public List<string> Purged { get; } = [];

		public IReadOnlyCollection<string> Names => [.. _secrets.Keys];

		public Task<string?> TryGetAsync(string name, CancellationToken cancellationToken) =>
			Task.FromResult(_secrets.TryGetValue(name, out var value) ? value : null);

		public Task SetAsync(
			string name,
			string value,
			IReadOnlyDictionary<string, string> tags,
			CancellationToken cancellationToken
		)
		{
			_secrets[name] = value;

			return Task.CompletedTask;
		}

		public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken) =>
			Task.FromResult(_secrets.TryRemove(name, out _));

		public Task PurgeAsync(string name, CancellationToken cancellationToken)
		{
			if (PurgeForbidden)
				throw new RequestFailedException(403, "Purge is not allowed on this vault.");

			Purged.Add(name);

			return Task.CompletedTask;
		}
	}
}
