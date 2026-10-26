using System.Collections.Concurrent;
using Purview.EventSourcing.Cryptography;

namespace Purview.EventSourcing.AzureKeyVault;

/// <summary>
/// The envelope key store: per-subject keys wrapped by a vault-held key.
/// </summary>
/// <remarks>
/// Byte arrays are compared as base64 rather than with IsEquivalentTo, which is order-insensitive:
/// the wrapping stub reverses its input, so an equivalence check would pass even if the store never
/// unwrapped anything.
/// <para>The property that matters is that the durable store never holds a usable key. If it did, the
/// whole model would be pointless - a stolen database would be readable without the vault.</para>
/// </remarks>
public sealed class AzureKeyVaultEnvelopeEventCryptoKeyStoreTests
{
	[Test]
	public async Task GetOrCreateAsync_StoresTheKeyWrapped_NeverInPlaintext()
	{
		SuppliedKeyStoreStub inner = new();
		AzureKeyVaultEnvelopeEventCryptoKeyStore store = new(inner, new MarkingWrappingStub());

		var key = await store.GetOrCreateAsync("subject-1", CancellationToken.None);
		var stored = await inner.TryGetAsync("subject-1", CancellationToken.None);

		await Assert.That(stored).IsNotNull();
		await Assert.That(Convert.ToBase64String(stored!)).IsNotEqualTo(Convert.ToBase64String(key));
		await Assert.That(stored!.Take(MarkingWrappingStub.Marker.Length)).IsEquivalentTo(MarkingWrappingStub.Marker);
	}

	[Test]
	public async Task TryGetAsync_UnwrapsWhatWasStored()
	{
		SuppliedKeyStoreStub inner = new();
		AzureKeyVaultEnvelopeEventCryptoKeyStore store = new(inner, new MarkingWrappingStub());

		var created = await store.GetOrCreateAsync("subject-1", CancellationToken.None);
		var read = await store.TryGetAsync("subject-1", CancellationToken.None);

		await Assert.That(Convert.ToBase64String(read!)).IsEqualTo(Convert.ToBase64String(created));
	}

	[Test]
	public async Task GetOrCreateAsync_CalledTwice_ReturnsTheSameKey()
	{
		SuppliedKeyStoreStub inner = new();
		AzureKeyVaultEnvelopeEventCryptoKeyStore store = new(inner, new MarkingWrappingStub());

		var first = await store.GetOrCreateAsync("s", CancellationToken.None);
		var second = await store.GetOrCreateAsync("s", CancellationToken.None);

		await Assert.That(Convert.ToBase64String(first)).IsEqualTo(Convert.ToBase64String(second));
	}

	// The loser of a concurrent create must use the winner's key, not the one it generated: payloads
	// encrypted under a key that was never stored could never be decrypted.
	[Test]
	public async Task GetOrCreateAsync_WhenAnotherCallerWonTheCreate_UsesTheStoredKey()
	{
		SuppliedKeyStoreStub inner = new();
		MarkingWrappingStub wrapping = new();
		AzureKeyVaultEnvelopeEventCryptoKeyStore store = new(inner, wrapping);

		// Seed the store as if another caller got there first.
		var winner = AesGcmEventPayloadProtector.CreateKey();
		await inner.GetOrAddAsync(
			"s",
			await wrapping.WrapAsync(winner, CancellationToken.None),
			CancellationToken.None
		);

		var key = await store.GetOrCreateAsync("s", CancellationToken.None);

		await Assert.That(Convert.ToBase64String(key)).IsEqualTo(Convert.ToBase64String(winner));
	}

	[Test]
	public async Task TryGetAsync_GivenNoKey_ReturnsNullWithoutUnwrapping()
	{
		SuppliedKeyStoreStub inner = new();
		MarkingWrappingStub wrapping = new();
		AzureKeyVaultEnvelopeEventCryptoKeyStore store = new(inner, wrapping);

		var key = await store.TryGetAsync("never-created", CancellationToken.None);

		await Assert.That(key).IsNull();
		await Assert.That(wrapping.UnwrapCount).IsEqualTo(0);
	}

	// Erasure deletes the wrapped key. The vault's wrapping key protects every other subject, so it
	// must survive.
	[Test]
	public async Task ShredAsync_RemovesTheWrappedKey()
	{
		SuppliedKeyStoreStub inner = new();
		AzureKeyVaultEnvelopeEventCryptoKeyStore store = new(inner, new MarkingWrappingStub());

		await store.GetOrCreateAsync("s", CancellationToken.None);
		var shredded = await store.ShredAsync("s", CancellationToken.None);
		var afterwards = await store.TryGetAsync("s", CancellationToken.None);

		await Assert.That(shredded).IsTrue();
		await Assert.That(afterwards).IsNull();
	}

	[Test]
	public async Task ShredAsync_GivenNoKey_ReportsThatNothingWasErased()
	{
		SuppliedKeyStoreStub inner = new();
		AzureKeyVaultEnvelopeEventCryptoKeyStore store = new(inner, new MarkingWrappingStub());

		await Assert.That(await store.ShredAsync("absent", CancellationToken.None)).IsFalse();
	}

	// Stands in for the vault. Wrapping prefixes a marker and unwrapping demands it, so unwrapping
	// something that was never wrapped fails loudly. A symmetric stand-in - byte reversal, say - is
	// its own inverse, which makes storing the plaintext key indistinguishable from wrapping it and
	// lets the test pass when the store never wraps at all.
	sealed class MarkingWrappingStub : IKeyVaultKeyWrapping
	{
		public static readonly byte[] Marker = [0xAB, 0xCD];

		public int UnwrapCount { get; private set; }

		public Task<byte[]> WrapAsync(byte[] key, CancellationToken cancellationToken) =>
			Task.FromResult<byte[]>([.. Marker, .. key]);

		public Task<byte[]> UnwrapAsync(byte[] wrappedKey, CancellationToken cancellationToken)
		{
			UnwrapCount++;

			if (wrappedKey.Length < Marker.Length || !wrappedKey.Take(Marker.Length).SequenceEqual(Marker))
				throw new InvalidOperationException("Asked to unwrap something that was never wrapped.");

			return Task.FromResult(wrappedKey[Marker.Length..]);
		}
	}

	sealed class SuppliedKeyStoreStub : ISuppliedKeyEventCryptoKeyStore
	{
		readonly ConcurrentDictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

		public Task<byte[]> GetOrAddAsync(
			string dataSubjectId,
			byte[] candidateKey,
			CancellationToken cancellationToken
		) => Task.FromResult(_keys.GetOrAdd(dataSubjectId, candidateKey));

		public Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			GetOrAddAsync(dataSubjectId, AesGcmEventPayloadProtector.CreateKey(), cancellationToken);

		public Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			Task.FromResult(_keys.TryGetValue(dataSubjectId, out var key) ? key : null);

		public Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			Task.FromResult(_keys.TryRemove(dataSubjectId, out _));
	}
}
