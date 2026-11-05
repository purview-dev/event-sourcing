namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// An <see cref="IEventCryptoKeyStore"/> that can hold more than one key generation per data
/// subject, so a key can be rotated without rewriting history.
/// </summary>
/// <remarks>
/// <para>
/// Rotation limits the blast radius of a future key compromise: payloads written after it use the
/// new key. It does <strong>not</strong> re-encrypt existing events, so older generations must be
/// retained or everything written under them becomes unreadable.
/// </para>
/// <para>
/// Rotation and erasure stay distinct. <see cref="IEventCryptoKeyStore.ShredAsync"/> removes
/// <em>every</em> generation for a subject - a shred that left an older key behind would not be an
/// erasure at all.
/// </para>
/// <para>
/// Optional: a store that cannot hold multiple generations simply does not implement this, and
/// payloads are written without a key identifier exactly as before.
/// </para>
/// </remarks>
public interface IRotatingEventCryptoKeyStore : IEventCryptoKeyStore
{
	/// <summary>
	/// Whether rotation is actually available.
	/// </summary>
	/// <remarks>
	/// A decorator implements this interface to pass rotation through, but can only support it when
	/// the store it wraps does. Check this rather than the interface alone.
	/// </remarks>
	bool SupportsRotation { get; }

	/// <summary>
	/// Gets the subject's current key, creating the first generation when there is none.
	/// </summary>
	/// <param name="dataSubjectId">The subject the key belongs to.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The current key and the identifier recorded alongside payloads encrypted with it.</returns>
	Task<(string KeyId, byte[] Key)> GetCurrentAsync(string dataSubjectId, CancellationToken cancellationToken);

	/// <summary>
	/// Gets a specific generation of a subject's key, or <see langword="null"/> when it has been
	/// erased or never existed.
	/// </summary>
	/// <param name="dataSubjectId">The subject the key belongs to.</param>
	/// <param name="keyId">The generation recorded on the payload.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	Task<byte[]?> TryGetAsync(string dataSubjectId, string keyId, CancellationToken cancellationToken);

	/// <summary>
	/// Creates a new current generation for the subject, retaining the previous ones.
	/// </summary>
	/// <param name="dataSubjectId">The subject to rotate.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The identifier of the new current key.</returns>
	Task<string> RotateAsync(string dataSubjectId, CancellationToken cancellationToken);
}
