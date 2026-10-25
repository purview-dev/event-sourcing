using System.Text.RegularExpressions;
using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[Scalar]
public readonly partial record struct EmailAddress
{
	public string Value { get; }

	/// <summary>
	/// The part after the '@', or an empty string when there is no value to read.
	/// </summary>
	/// <remarks>
	/// A record struct is always constructible as <c>default</c>, and validators read computed members
	/// before a value is assigned, so this cannot assume <see cref="Value"/> is a well-formed address.
	/// </remarks>
	public string Domain
	{
		get
		{
			var separator = Value?.IndexOf('@', StringComparison.Ordinal) ?? -1;

			return separator < 0 ? string.Empty : Value![(separator + 1)..];
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase")]
	static partial void OnNormalize(ref string value) => value = value?.Trim().ToLowerInvariant()!;

	static partial void OnValidate(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new ArgumentException("Email address cannot be empty.", nameof(value));
		if (!EmailRegex().IsMatch(value))
			throw new ArgumentException("Invalid email address format.", nameof(value));
	}

	[GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
	private static partial Regex EmailRegex();
}
