using System.Text.Json.Serialization;

namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// The stored form of an encrypted event payload.
/// </summary>
/// <remarks>
/// Written in place of the plaintext payload, so no provider needs a new column: every store already
/// persists the payload as text. The subject identifier travels with the ciphertext because replay has
/// to know which key to ask for before it can read anything.
/// </remarks>
public sealed record ProtectedPayload
{
	/// <summary>
	/// Discriminates an encrypted payload from a plaintext one.
	/// </summary>
	/// <remarks>
	/// Deliberately unlikely to collide with a real event property, so an unprotected payload is never
	/// mistaken for a protected one.
	/// </remarks>
	public const string MarkerPropertyName = "__protected";

	/// <summary>Always <see langword="true"/>; present so the payload can be recognised before parsing.</summary>
	[JsonPropertyName(MarkerPropertyName)]
	public bool Protected { get; init; } = true;

	/// <summary>The algorithm the payload was encrypted with.</summary>
	[JsonPropertyName("alg")]
	public string Algorithm { get; init; } = AesGcmEventPayloadProtector.AlgorithmName;

	/// <summary>The subject whose key the payload was encrypted with.</summary>
	[JsonPropertyName("sub")]
	public string DataSubjectId { get; init; } = default!;

	/// <summary>
	/// The generation of the subject's key the payload was encrypted with.
	/// </summary>
	/// <remarks>
	/// Absent when the key store does not hold multiple generations, which is what every payload
	/// written before rotation existed looks like. A reader treats a missing identifier as "the
	/// subject's only key", so the addition is backward compatible and needs no upcaster.
	/// </remarks>
	[JsonPropertyName("kid")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? KeyId { get; init; }

	/// <summary>The base64 nonce.</summary>
	[JsonPropertyName("n")]
	public string Nonce { get; init; } = default!;

	/// <summary>The base64 authentication tag.</summary>
	[JsonPropertyName("t")]
	public string Tag { get; init; } = default!;

	/// <summary>The base64 ciphertext.</summary>
	[JsonPropertyName("c")]
	public string CipherText { get; init; } = default!;
}
