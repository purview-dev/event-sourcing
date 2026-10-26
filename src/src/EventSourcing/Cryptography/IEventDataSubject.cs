namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// Marks an event as carrying personal data belonging to a single data subject.
/// </summary>
/// <remarks>
/// The payload of an event implementing this is encrypted with a key belonging to
/// <see cref="DataSubjectId"/>. Erasing that subject deletes the key, which renders their payloads
/// unreadable everywhere they appear without rewriting or deleting any event - the stream stays
/// append-only and every other subject's data is untouched.
/// </remarks>
public interface IEventDataSubject
{
	/// <summary>
	/// Identifies the person the event's personal data belongs to.
	/// </summary>
	/// <remarks>
	/// Must be stable for the lifetime of the subject: it is the key identifier, so a value that changes
	/// orphans the earlier events. It is recorded in clear alongside the ciphertext, so it must not
	/// itself be personal data - use a surrogate identifier, not an email address.
	/// </remarks>
	string DataSubjectId { get; }
}
