using System.Text;

namespace Purview.EventSourcing.AzureStorage;

/// <summary>
/// Encodes arbitrary text into a value that is legal in an Azure Table partition or row key.
/// </summary>
/// <remarks>
/// <para>
/// Azure table keys cannot contain <c>/</c>, <c>\</c>, <c>#</c>, <c>?</c> or control characters, and
/// a composed key built by concatenation is ambiguous when a segment contains the separator.
/// Base64Url uses only characters that are legal in a key and never produces <c>|</c>, so a key
/// composed from encoded segments is both legal and unambiguous.
/// </para>
/// <para>
/// Shared so every side table in this provider encodes identically. Two encodings of the same
/// concept is how one tenant's row ends up colliding with another's.
/// </para>
/// </remarks>
static class AzureTableKeyEncoding
{
	/// <summary>The separator placed between encoded segments of a composed key.</summary>
	public const char SegmentSeparator = '|';

	/// <summary>Encodes one segment of a key.</summary>
	/// <param name="value">The value to encode.</param>
	public static string Encode(string value) =>
		Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
