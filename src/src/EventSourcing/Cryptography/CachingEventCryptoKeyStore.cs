using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// Caches another <see cref="IEventCryptoKeyStore"/> in process memory.
/// </summary>
/// <remarks>
/// <para>
/// Without this, every protect and unprotect is a round trip to the key store. Against a database
/// that is wasteful; against Azure Key Vault it is both slow and throttled, so caching is what makes
/// an external key store usable at volume.
/// </para>
/// <para>
/// <strong>A cached key outliving its erasure is a compliance failure, not a performance quirk.</strong>
/// <see cref="ShredAsync"/> therefore evicts before deleting and again afterwards: a concurrent read
/// can repopulate the entry between the two, and the second eviction is what removes it.
/// </para>
/// <para>
/// <strong>The cache is per process.</strong> Another instance that has the key cached keeps serving
/// it until its own entry expires, so <see cref="EventCryptoOptions.KeyCacheDuration"/> is the upper
/// bound on how long an erasure takes to take effect across a cluster. Keep it short, and treat the
/// duration as a compliance parameter rather than a tuning one.
/// </para>
/// <para>
/// Misses are not cached. A negative entry would keep a subject unreadable after their key was
/// legitimately created, which looks exactly like erasure and is far harder to diagnose.
/// </para>
/// </remarks>
public sealed class CachingEventCryptoKeyStore
	: IRotatingEventCryptoKeyStore,
		IEventCryptoKeyStoreDecorator,
		IDisposable
{
	readonly IEventCryptoKeyStore _inner;
	readonly ICurrentTenant _currentTenant;
	readonly MemoryCache _cache;
	readonly TimeSpan _duration;

	/// <summary>Creates the caching store.</summary>
	/// <param name="inner">The store to cache.</param>
	/// <param name="options">The crypto options carrying the cache duration and size.</param>
	/// <param name="currentTenant">The tenant scope, which forms part of the cache key.</param>
	public CachingEventCryptoKeyStore(
		IEventCryptoKeyStore inner,
		IOptions<EventCryptoOptions> options,
		ICurrentTenant? currentTenant = null
	)
	{
		ArgumentNullException.ThrowIfNull(inner);
		ArgumentNullException.ThrowIfNull(options);

		_inner = inner;
		_currentTenant = currentTenant ?? new SystemCurrentTenant();
		_duration = options.Value.KeyCacheDuration;
		_cache = new(new MemoryCacheOptions { SizeLimit = options.Value.KeyCacheSizeLimit });
	}

	/// <inheritdoc/>
	public IEventCryptoKeyStore Inner => _inner;

	/// <inheritdoc/>
	public bool SupportsRotation => _inner is IRotatingEventCryptoKeyStore { SupportsRotation: true };

	/// <inheritdoc/>
	/// <remarks>
	/// The current generation is cached like any other key, so a rotation on another instance takes
	/// up to <see cref="EventCryptoOptions.KeyCacheDuration"/> to be picked up here. That delays the
	/// benefit of rotating; it does not break anything, because payloads written under the previous
	/// generation stay readable.
	/// </remarks>
	public async Task<(string KeyId, byte[] Key)> GetCurrentAsync(
		string dataSubjectId,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		var cacheKey = CurrentCacheKeyFor(dataSubjectId);
		if (_cache.TryGetValue(cacheKey, out (string KeyId, byte[] Key) cached) && cached.Key is not null)
			return cached;

		var current = await RotatingInner.GetCurrentAsync(dataSubjectId, cancellationToken);
		_cache.Set(
			cacheKey,
			current,
			new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _duration, Size = current.Key.Length }
		);

		return current;
	}

	/// <inheritdoc/>
	public async Task<byte[]?> TryGetAsync(string dataSubjectId, string keyId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);
		ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

		var cacheKey = GenerationCacheKeyFor(dataSubjectId, keyId);
		if (_cache.TryGetValue(cacheKey, out byte[]? cached) && cached is not null)
			return cached;

		var fetched = await RotatingInner.TryGetAsync(dataSubjectId, keyId, cancellationToken);
		if (fetched is not null)
			Store(cacheKey, fetched);

		return fetched;
	}

	/// <inheritdoc/>
	public async Task<string> RotateAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		// Evict the cached current generation before and after, for the same reason as a shred: a
		// reader racing the rotation can put the previous one back.
		_cache.Remove(CurrentCacheKeyFor(dataSubjectId));
		try
		{
			return await RotatingInner.RotateAsync(dataSubjectId, cancellationToken);
		}
		finally
		{
			_cache.Remove(CurrentCacheKeyFor(dataSubjectId));
			_cache.Remove(CacheKeyFor(dataSubjectId));
		}
	}

	IRotatingEventCryptoKeyStore RotatingInner =>
		_inner as IRotatingEventCryptoKeyStore
		?? throw new NotSupportedException(
			$"The configured key store '{_inner.GetType().Name}' does not support key rotation."
		);

	(string Tenant, string Subject, string Marker) CurrentCacheKeyFor(string dataSubjectId) =>
		(TenantColumn.Encode(_currentTenant.Current), dataSubjectId, "current");

	(string Tenant, string Subject, string KeyId) GenerationCacheKeyFor(string dataSubjectId, string keyId) =>
		(TenantColumn.Encode(_currentTenant.Current), dataSubjectId, keyId);

	/// <inheritdoc/>
	public async Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		var key = CacheKeyFor(dataSubjectId);
		if (_cache.TryGetValue(key, out byte[]? cached) && cached is not null)
			return cached;

		var created = await _inner.GetOrCreateAsync(dataSubjectId, cancellationToken);
		Store(key, created);

		return created;
	}

	/// <inheritdoc/>
	public async Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		var key = CacheKeyFor(dataSubjectId);
		if (_cache.TryGetValue(key, out byte[]? cached) && cached is not null)
			return cached;

		var fetched = await _inner.TryGetAsync(dataSubjectId, cancellationToken);
		if (fetched is not null)
			Store(key, fetched);

		return fetched;
	}

	/// <inheritdoc/>
	public async Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		var key = CacheKeyFor(dataSubjectId);

		// Evict first so no reader can take a fresh copy while the delete is in flight, delete, then
		// evict again to remove anything a concurrent reader repopulated in between.
		_cache.Remove(key);
		try
		{
			return await _inner.ShredAsync(dataSubjectId, cancellationToken);
		}
		finally
		{
			_cache.Remove(key);
		}
	}

	void Store(object key, byte[] value) =>
		_cache.Set(
			key,
			value,
			new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _duration, Size = value.Length }
		);

	// The tenant is part of the key because the same subject identifier in two tenants is two
	// different people with two different keys.
	(string Tenant, string Subject) CacheKeyFor(string dataSubjectId) =>
		(TenantColumn.Encode(_currentTenant.Current), dataSubjectId);

	/// <inheritdoc/>
	public void Dispose() => _cache.Dispose();
}
