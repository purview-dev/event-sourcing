namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// Guards the crypto-shredding contract: a payload round-trips for its own subject, becomes unreadable
/// once that subject is erased, and leaves every other subject untouched.
/// </summary>
public sealed class AesGcmEventPayloadProtectorTests
{
	const string Payload = """{"Amount":42,"Name":"Ada"}""";

	static (AesGcmEventPayloadProtector Protector, InMemoryEventCryptoKeyStore Keys) Create()
	{
		InMemoryEventCryptoKeyStore keys = new();

		return (new AesGcmEventPayloadProtector(keys), keys);
	}

	[Test]
	public async Task ProtectAsync_ThenUnprotect_RoundTripsThePayload()
	{
		var (protector, _) = Create();

		var protectedPayload = await protector.ProtectAsync(Payload, "user-4712");

		await Assert.That(protector.IsProtected(protectedPayload)).IsTrue();
		await Assert.That(await protector.UnprotectAsync(protectedPayload)).IsEqualTo(Payload);
	}

	// The whole point: the ciphertext must not contain the plaintext, or erasing the key achieves nothing.
	[Test]
	public async Task ProtectAsync_DoesNotLeaveThePlaintextReadable()
	{
		var (protector, _) = Create();

		var protectedPayload = await protector.ProtectAsync(Payload, "user-4712");

		// Distinctive fragments, not short ones: a two-character value like "42" turns up in base64 by
		// chance and would make this pass or fail at random.
		await Assert.That(protectedPayload).DoesNotContain("Ada");
		await Assert.That(protectedPayload).DoesNotContain("Amount");
		await Assert.That(protectedPayload).DoesNotContain(Payload);
	}

	[Test]
	public async Task UnprotectAsync_AfterShredding_ReturnsNull()
	{
		var (protector, keys) = Create();
		var protectedPayload = await protector.ProtectAsync(Payload, "user-4712");

		var shredded = await keys.ShredAsync("user-4712", CancellationToken.None);

		await Assert.That(shredded).IsTrue();
		await Assert.That(await protector.UnprotectAsync(protectedPayload)).IsNull();
	}

	// Erasure must be surgical: one subject's erasure cannot take another's data with it.
	[Test]
	public async Task ShredAsync_LeavesOtherSubjectsReadable()
	{
		var (protector, keys) = Create();
		var erased = await protector.ProtectAsync(Payload, "user-4712");
		var retained = await protector.ProtectAsync(Payload, "user-9003");

		await keys.ShredAsync("user-4712", CancellationToken.None);

		await Assert.That(await protector.UnprotectAsync(erased)).IsNull();
		await Assert.That(await protector.UnprotectAsync(retained)).IsEqualTo(Payload);
	}

	// A payload written before protection was configured must still be readable.
	[Test]
	public async Task UnprotectAsync_GivenAnUnprotectedPayload_ReturnsItUnchanged()
	{
		var (protector, _) = Create();

		await Assert.That(await protector.UnprotectAsync(Payload)).IsEqualTo(Payload);
	}

	// The subject is bound as associated data, so ciphertext cannot be re-attributed to another subject.
	[Test]
	public async Task UnprotectAsync_GivenCipherTextReattributedToAnotherSubject_Fails()
	{
		var (protector, keys) = Create();
		var protectedPayload = await protector.ProtectAsync(Payload, "user-4712");

		// Give the other subject the same key, so only the bound subject id differs.
		var key = await keys.GetOrCreateAsync("user-4712", CancellationToken.None);
		StubKeyStore sharedKeys = new(key);
		AesGcmEventPayloadProtector attacker = new(sharedKeys);

		var reattributed = protectedPayload.Replace("user-4712", "user-9003", StringComparison.Ordinal);

		await Assert
			.That(async () => await attacker.UnprotectAsync(reattributed))
			.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>();
	}

	sealed class StubKeyStore(byte[] key) : IEventCryptoKeyStore
	{
		public Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			Task.FromResult(key);

		public Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			Task.FromResult<byte[]?>(key);

		public Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken) =>
			Task.FromResult(true);
	}
}
