namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// Stores the per-subject keys used to encrypt event payloads.
/// </summary>
/// <remarks>
/// The store is the erasure mechanism: deleting a key is the irreversible act, so a production
/// implementation should be backed by a key vault with its own access control and audit trail, and must
/// not be backed by anything that silently restores deleted entries from a backup.
/// </remarks>
public interface IEventCryptoKeyStore
{
	/// <summary>
	/// Gets the key for a subject, creating one if none exists.
	/// </summary>
	/// <param name="dataSubjectId">The subject the key belongs to.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>The subject's key.</returns>
	Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the key for a subject, or <see langword="null"/> when it has been erased or never existed.
	/// </summary>
	/// <param name="dataSubjectId">The subject the key belongs to.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>The subject's key, or <see langword="null"/>.</returns>
	/// <remarks>
	/// A missing key is an expected outcome during replay - it is what erasure looks like - so this
	/// returns null rather than throwing.
	/// </remarks>
	Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken);

	/// <summary>
	/// Erases a subject's key, rendering every payload encrypted with it permanently unreadable.
	/// </summary>
	/// <param name="dataSubjectId">The subject to erase.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns><see langword="true"/> when a key was erased; <see langword="false"/> when none existed.</returns>
	Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken);
}
