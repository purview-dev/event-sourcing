namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// Encrypts and decrypts event payloads for crypto-shredding.
/// </summary>
public interface IEventPayloadProtector
{
	/// <summary>
	/// Encrypts a payload under the subject's key.
	/// </summary>
	/// <param name="payload">The serialized event payload.</param>
	/// <param name="dataSubjectId">The subject whose key to use.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>The protected payload, to be stored in place of the plaintext.</returns>
	Task<string> ProtectAsync(string payload, string dataSubjectId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Decrypts a payload, or reports that its key has been erased.
	/// </summary>
	/// <param name="payload">The stored payload, protected or not.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>
	/// The plaintext payload; the payload unchanged when it was never protected; or <see langword="null"/>
	/// when the subject's key has been erased.
	/// </returns>
	Task<string?> UnprotectAsync(string payload, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reports whether a stored payload is protected.
	/// </summary>
	/// <param name="payload">The stored payload.</param>
	/// <returns><see langword="true"/> when the payload is encrypted.</returns>
	bool IsProtected(string payload);
}
