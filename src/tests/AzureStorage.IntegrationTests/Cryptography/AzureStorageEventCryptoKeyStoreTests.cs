using Microsoft.Extensions.Options;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.Fixtures.AzureStorage;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.AzureStorage.Cryptography;

/// <summary>
/// The durable crypto key store against real Azure Table Storage.
/// </summary>
/// <remarks>
/// Tenant separation comes first. A key is the most sensitive row in the system: reading another
/// tenant's key reads all of their protected events, and erasing it destroys their data
/// irreversibly. The same data-subject identifier in two tenants is two different people.
/// </remarks>
[ClassDataSource<TableEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class AzureStorageEventCryptoKeyStoreTests(TableEventStoreFixture fixture)
{
	[Test]
	public async Task TryGetAsync_GivenAnotherTenantsSubject_DoesNotReturnTheKey(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var subject = $"{Guid.NewGuid():N}";

		await AsTenantAsync("acme", () => store.GetOrCreateAsync(subject, cancellationToken));

		var other = await AsTenantAsync("globex", () => store.TryGetAsync(subject, cancellationToken));

		await Assert.That(other).IsNull();
	}

	[Test]
	public async Task GetOrCreateAsync_GivenTheSameSubjectInTwoTenants_CreatesDifferentKeys(
		CancellationToken cancellationToken
	)
	{
		using var store = CreateStore();
		var subject = $"{Guid.NewGuid():N}";

		var acme = await AsTenantAsync("acme", () => store.GetOrCreateAsync(subject, cancellationToken));
		var globex = await AsTenantAsync("globex", () => store.GetOrCreateAsync(subject, cancellationToken));

		await Assert.That(acme).IsNotEquivalentTo(globex);
	}

	// Erasing one tenant's subject must not erase another tenant's subject of the same name.
	[Test]
	public async Task ShredAsync_GivenAnotherTenantsSubject_LeavesTheOwnersKeyIntact(
		CancellationToken cancellationToken
	)
	{
		using var store = CreateStore();
		var subject = $"{Guid.NewGuid():N}";

		var owner = await AsTenantAsync("acme", () => store.GetOrCreateAsync(subject, cancellationToken));
		var shredded = await AsTenantAsync("globex", () => store.ShredAsync(subject, cancellationToken));
		var stillThere = await AsTenantAsync("acme", () => store.TryGetAsync(subject, cancellationToken));

		await Assert.That(shredded).IsFalse();
		await Assert.That(stillThere).IsEquivalentTo(owner);
	}

	[Test]
	public async Task GetOrCreateAsync_CalledTwice_ReturnsTheSameKey(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var subject = $"{Guid.NewGuid():N}";

		var first = await AsTenantAsync("acme", () => store.GetOrCreateAsync(subject, cancellationToken));
		var second = await AsTenantAsync("acme", () => store.GetOrCreateAsync(subject, cancellationToken));

		await Assert.That(first).IsEquivalentTo(second);
	}

	// Concurrent first-use must converge on one key. If two callers each created their own, whichever
	// lost would have written payloads nothing can decrypt.
	[Test]
	public async Task GetOrCreateAsync_GivenConcurrentFirstUse_ConvergesOnOneKey(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var subject = $"{Guid.NewGuid():N}";

		var keys = await AsTenantAsync(
			"acme",
			async () =>
				await Task.WhenAll(
					Enumerable.Range(0, 8).Select(_ => store.GetOrCreateAsync(subject, cancellationToken))
				)
		);

		await Assert.That(keys.Select(Convert.ToBase64String).Distinct().Count()).IsEqualTo(1);
	}

	[Test]
	public async Task ShredAsync_GivenAnExistingKey_RemovesItPermanently(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var subject = $"{Guid.NewGuid():N}";

		await AsTenantAsync("acme", () => store.GetOrCreateAsync(subject, cancellationToken));
		var shredded = await AsTenantAsync("acme", () => store.ShredAsync(subject, cancellationToken));
		var afterwards = await AsTenantAsync("acme", () => store.TryGetAsync(subject, cancellationToken));

		await Assert.That(shredded).IsTrue();
		await Assert.That(afterwards).IsNull();
	}

	[Test]
	public async Task ShredAsync_GivenNoKey_ReportsThatNothingWasErased(CancellationToken cancellationToken)
	{
		using var store = CreateStore();

		var shredded = await AsTenantAsync("acme", () => store.ShredAsync($"{Guid.NewGuid():N}", cancellationToken));

		await Assert.That(shredded).IsFalse();
	}

	// End to end: the key store is only useful if shredding actually makes a payload unreadable.
	[Test]
	public async Task UnprotectAsync_AfterTheKeyIsShredded_ReportsErasure(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		AesGcmEventPayloadProtector protector = new(store);
		var subject = $"{Guid.NewGuid():N}";

		var protectedPayload = await AsTenantAsync(
			"acme",
			() => protector.ProtectAsync("""{"name":"a-distinctive-value"}""", subject, cancellationToken)
		);

		var before = await AsTenantAsync("acme", () => protector.UnprotectAsync(protectedPayload, cancellationToken));
		await AsTenantAsync("acme", () => store.ShredAsync(subject, cancellationToken));
		var after = await AsTenantAsync("acme", () => protector.UnprotectAsync(protectedPayload, cancellationToken));

		await Assert.That(before).IsEqualTo("""{"name":"a-distinctive-value"}""");
		await Assert.That(after).IsNull();
	}

	static async Task<T> AsTenantAsync<T>(string tenant, Func<Task<T>> operation)
	{
		using (AmbientCurrentTenant.BeginScope(TenantScope.For(tenant)))
			return await operation();
	}

	AzureStorageEventCryptoKeyStore CreateStore() =>
		new(
			Options.Create(
				new AzureStorageEventCryptoKeyStoreOptions
				{
					ConnectionString = fixture.ConnectionString,
					TableName = $"CryptoKeys{Guid.NewGuid():N}",
				}
			),
			Options.Create(new AzureStorageEventStoreOptions { ConnectionString = fixture.ConnectionString }),
			new AmbientCurrentTenant()
		);
}
