using System.Collections.Concurrent;

namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// An in-memory <see cref="IEventCryptoKeyStore"/> for local development and tests.
/// </summary>
/// <remarks>
/// Keys live only for the lifetime of the process, so every payload becomes unreadable on restart.
/// That makes it unusable in production by construction, which is the intent: a key store is the one
/// component where a convenient default would be actively harmful.
/// </remarks>
public sealed class InMemoryEventCryptoKeyStore : IEventCryptoKeyStore, INonDurableEventCryptoKeyStore
{
	readonly ConcurrentDictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

	/// <inheritdoc/>
	public Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		return Task.FromResult(_keys.GetOrAdd(dataSubjectId, _ => AesGcmEventPayloadProtector.CreateKey()));
	}

	/// <inheritdoc/>
	public Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		return Task.FromResult(_keys.TryGetValue(dataSubjectId, out var key) ? key : null);
	}

	/// <inheritdoc/>
	public Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		return Task.FromResult(_keys.TryRemove(dataSubjectId, out _));
	}
}
