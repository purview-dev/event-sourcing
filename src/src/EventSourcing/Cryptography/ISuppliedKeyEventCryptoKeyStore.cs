namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// An <see cref="IEventCryptoKeyStore"/> that can persist key material supplied by the caller,
/// rather than generating its own.
/// </summary>
/// <remarks>
/// <para>
/// Needed for envelope encryption, where the bytes stored are a <em>wrapped</em> key: the store must
/// persist exactly what it is given, because it cannot produce a wrapped key itself and must never
/// hold the plaintext one.
/// </para>
/// <para>
/// The contract is get-or-add, not set: concurrent callers must converge on one stored value.
/// Whichever caller wins, both must end up using what the store holds - if two callers each kept
/// their own key, payloads written by the loser could never be decrypted.
/// </para>
/// </remarks>
public interface ISuppliedKeyEventCryptoKeyStore : IEventCryptoKeyStore
{
	/// <summary>
	/// Returns the stored key material for a subject, storing <paramref name="candidateKey"/> first
	/// if there is none.
	/// </summary>
	/// <param name="dataSubjectId">The subject the key belongs to.</param>
	/// <param name="candidateKey">The material to store when the subject has none.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>
	/// The stored material, which is <paramref name="candidateKey"/> only when this caller won the
	/// race to create it.
	/// </returns>
	Task<byte[]> GetOrAddAsync(string dataSubjectId, byte[] candidateKey, CancellationToken cancellationToken);
}
