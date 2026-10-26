using System.Security.Cryptography;
using System.Text;
using Purview.EventSourcing.Serialization;

namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// Encrypts event payloads with AES-GCM under a per-subject key.
/// </summary>
/// <remarks>
/// AES-GCM is authenticated, so a payload that has been altered fails to decrypt rather than yielding
/// plausible rubbish. The subject identifier is bound into the encryption as associated data, so moving
/// ciphertext from one subject's event to another's is detected rather than silently decrypting.
/// </remarks>
/// <param name="keyStore">Supplies and erases the per-subject keys.</param>
public sealed class AesGcmEventPayloadProtector(IEventCryptoKeyStore keyStore) : IEventPayloadProtector
{
	/// <summary>
	/// The algorithm identifier written into <see cref="ProtectedPayload.Algorithm"/>.
	/// </summary>
	public const string AlgorithmName = "AES-GCM-256";

	/// <summary>
	/// The key length in bytes.
	/// </summary>
	public const int KeySizeInBytes = 32;

	/// <inheritdoc/>
	public async Task<string> ProtectAsync(
		string payload,
		string dataSubjectId,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(payload);
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		// A rotating store records which generation was used, so a later rotation does not make this
		// payload unreadable. A store without rotation writes no identifier, which is exactly what
		// every payload written before rotation existed looks like.
		string? keyId = null;
		byte[] key;
		if (Rotating is { } rotating)
			(keyId, key) = await rotating.GetCurrentAsync(dataSubjectId, cancellationToken);
		else
			key = await keyStore.GetOrCreateAsync(dataSubjectId, cancellationToken);

		var plaintext = Encoding.UTF8.GetBytes(payload);

		var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
		RandomNumberGenerator.Fill(nonce);

		var cipherText = new byte[plaintext.Length];
		var tag = new byte[AesGcm.TagByteSizes.MaxSize];

		using AesGcm aes = new(key, tag.Length);
		aes.Encrypt(nonce, plaintext, cipherText, tag, Encoding.UTF8.GetBytes(dataSubjectId));

		return EventStoreSerializationHelpers.Serialize(
			new ProtectedPayload
			{
				DataSubjectId = dataSubjectId,
				KeyId = keyId,
				Nonce = Convert.ToBase64String(nonce),
				Tag = Convert.ToBase64String(tag),
				CipherText = Convert.ToBase64String(cipherText),
			}
		);
	}

	/// <inheritdoc/>
	public async Task<string?> UnprotectAsync(string payload, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(payload);

		if (!IsProtected(payload))
			return payload;

		var envelope =
			EventStoreSerializationHelpers.Deserialize<ProtectedPayload>(payload)
			?? throw new InvalidOperationException("The payload is marked as protected but could not be read.");

		// A payload carrying a generation must be decrypted with that generation, not the current
		// one - otherwise every rotation would silently break everything written before it.
		var key =
			envelope.KeyId is { } writtenWith && Rotating is { } rotating
				? await rotating.TryGetAsync(envelope.DataSubjectId, writtenWith, cancellationToken)
				: await keyStore.TryGetAsync(envelope.DataSubjectId, cancellationToken);
		if (key is null)
			// The subject has been erased. Expected, not exceptional: the caller turns this into a
			// skipped event so replay continues and the gap stays visible.
			return null;

		var nonce = Convert.FromBase64String(envelope.Nonce);
		var tag = Convert.FromBase64String(envelope.Tag);
		var cipherText = Convert.FromBase64String(envelope.CipherText);
		var plaintext = new byte[cipherText.Length];

		using AesGcm aes = new(key, tag.Length);
		aes.Decrypt(nonce, cipherText, tag, plaintext, Encoding.UTF8.GetBytes(envelope.DataSubjectId));

		return Encoding.UTF8.GetString(plaintext);
	}

	// No unwrapping here: a decorator implements the interface and reports whether the store it wraps
	// actually supports rotation, which is what SupportsRotation is for. Unwrapping would find the
	// real store and then have to call through the decorator anyway, losing its caching.
	IRotatingEventCryptoKeyStore? Rotating =>
		keyStore is IRotatingEventCryptoKeyStore { SupportsRotation: true } rotating ? rotating : null;

	/// <inheritdoc/>
	public bool IsProtected(string payload) =>
		payload is not null && payload.Contains(ProtectedPayload.MarkerPropertyName, StringComparison.Ordinal);

	/// <summary>
	/// Creates a new random key.
	/// </summary>
	/// <returns>A key of <see cref="KeySizeInBytes"/> bytes.</returns>
	public static byte[] CreateKey() => RandomNumberGenerator.GetBytes(KeySizeInBytes);
}
