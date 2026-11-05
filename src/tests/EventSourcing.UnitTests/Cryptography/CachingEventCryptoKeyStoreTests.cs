using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// The process-local key cache.
/// </summary>
/// <remarks>
/// The behaviour worth protecting is that a cached key never outlives its erasure. Everything else
/// here is a performance concern; that one is a compliance failure.
/// </remarks>
public sealed class CachingEventCryptoKeyStoreTests
{
	[Test]
	public async Task TryGetAsync_WhenCached_DoesNotAskTheInnerStoreAgain()
	{
		CountingKeyStoreStub inner = new();
		using var store = CreateStore(inner);

		await store.GetOrCreateAsync("s", CancellationToken.None);
		await store.TryGetAsync("s", CancellationToken.None);
		await store.TryGetAsync("s", CancellationToken.None);

		await Assert.That(inner.TryGetCount).IsEqualTo(0);
	}

	[Test]
	public async Task TryGetAsync_ReturnsTheSameKeyAsTheInnerStore()
	{
		CountingKeyStoreStub inner = new();
		using var store = CreateStore(inner);

		var created = await store.GetOrCreateAsync("s", CancellationToken.None);
		var direct = await inner.TryGetAsync("s", CancellationToken.None);

		await Assert.That(Convert.ToBase64String(created)).IsEqualTo(Convert.ToBase64String(direct!));
	}

	// The whole point. A key served from cache after it was erased is a subject who was told their
	// data was deleted and whose data is still readable.
	[Test]
	public async Task ShredAsync_EvictsTheCachedKey()
	{
		CountingKeyStoreStub inner = new();
		using var store = CreateStore(inner);

		await store.GetOrCreateAsync("s", CancellationToken.None);
		await store.ShredAsync("s", CancellationToken.None);
		var afterwards = await store.TryGetAsync("s", CancellationToken.None);

		await Assert.That(afterwards).IsNull();
	}

	// A reader racing the shred can repopulate the entry after the first eviction, which is why the
	// store evicts again once the delete has happened.
	[Test]
	public async Task ShredAsync_WhenAReaderRepopulatesDuringTheDelete_StillEvicts()
	{
		CountingKeyStoreStub inner = new();
		using var store = CreateStore(inner);

		await store.GetOrCreateAsync("s", CancellationToken.None);

		// Simulate the race: the inner store repopulates the cache from inside the delete.
		inner.OnShred = () => store.TryGetAsync("s", CancellationToken.None).GetAwaiter().GetResult();

		await store.ShredAsync("s", CancellationToken.None);
		var afterwards = await store.TryGetAsync("s", CancellationToken.None);

		await Assert.That(afterwards).IsNull();
	}

	[Test]
	public async Task TryGetAsync_GivenAMiss_DoesNotCacheTheAbsence()
	{
		CountingKeyStoreStub inner = new();
		using var store = CreateStore(inner);

		await store.TryGetAsync("s", CancellationToken.None);
		var created = await store.GetOrCreateAsync("s", CancellationToken.None);
		var read = await store.TryGetAsync("s", CancellationToken.None);

		await Assert.That(read).IsNotNull();
		await Assert.That(Convert.ToBase64String(read!)).IsEqualTo(Convert.ToBase64String(created));
	}

	// Two tenants with the same subject id are two different people, so the cache must not hand one
	// tenant's key to the other.
	[Test]
	public async Task Cache_IsScopedByTenant()
	{
		TenantAwareKeyStoreStub inner = new();
		using var store = CreateStore(inner, new AmbientCurrentTenant());

		var acme = await AsTenantAsync("acme", () => store.GetOrCreateAsync("s", CancellationToken.None));
		var globex = await AsTenantAsync("globex", () => store.GetOrCreateAsync("s", CancellationToken.None));

		await Assert.That(Convert.ToBase64String(acme)).IsNotEqualTo(Convert.ToBase64String(globex));
	}

	// The cache wraps the real store, so a check made against the outermost object would stop seeing
	// a non-durable one underneath.
	[Test]
	public async Task StartupCheck_SeesThroughTheCacheToANonDurableStore()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddSingleton<IHostEnvironment>(new ProductionEnvironment());
		services.AddEventSourcing().AddInMemoryEventCryptoShredding();

		await using var provider = services.BuildServiceProvider();
		var check = provider.GetServices<IHostedService>().OfType<NonDurableKeyStoreStartupCheck>().Single();

		await Assert.That(provider.GetRequiredService<IEventCryptoKeyStore>()).IsTypeOf<CachingEventCryptoKeyStore>();
		await Assert
			.That(async () => await check.StartAsync(CancellationToken.None))
			.Throws<InvalidOperationException>();
	}

	static async Task<T> AsTenantAsync<T>(string tenant, Func<Task<T>> operation)
	{
		using (AmbientCurrentTenant.BeginScope(TenantScope.For(tenant)))
			return await operation();
	}

	static CachingEventCryptoKeyStore CreateStore(IEventCryptoKeyStore inner, ICurrentTenant? currentTenant = null) =>
		new(inner, Options.Create(new EventCryptoOptions()), currentTenant);

	sealed class CountingKeyStoreStub : IEventCryptoKeyStore
	{
		readonly ConcurrentDictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

		public int TryGetCount { get; private set; }

		public Action? OnShred { get; set; }

		public Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			Task.FromResult(_keys.GetOrAdd(dataSubjectId, _ => AesGcmEventPayloadProtector.CreateKey()));

		public Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken)
		{
			TryGetCount++;

			return Task.FromResult(_keys.TryGetValue(dataSubjectId, out var key) ? key : null);
		}

		public Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken)
		{
			// Invoked before the removal, so the reader sees a live key and repopulates the cache -
			// which is the race the second eviction exists for. Invoking it after would find nothing
			// to cache and the test would prove nothing.
			OnShred?.Invoke();
			var removed = _keys.TryRemove(dataSubjectId, out _);

			return Task.FromResult(removed);
		}
	}

	sealed class TenantAwareKeyStoreStub : IEventCryptoKeyStore
	{
		readonly ConcurrentDictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

		static string KeyFor(string dataSubjectId) =>
			$"{TenantColumn.Encode(new AmbientCurrentTenant().Current)}|{dataSubjectId}";

		public Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			Task.FromResult(_keys.GetOrAdd(KeyFor(dataSubjectId), _ => AesGcmEventPayloadProtector.CreateKey()));

		public Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			Task.FromResult(_keys.TryGetValue(KeyFor(dataSubjectId), out var key) ? key : null);

		public Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			Task.FromResult(_keys.TryRemove(KeyFor(dataSubjectId), out _));
	}

	sealed class ProductionEnvironment : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = "Production";

		public string ApplicationName { get; set; } = "Tests";

		public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

		public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
			new Microsoft.Extensions.FileProviders.NullFileProvider();
	}
}
