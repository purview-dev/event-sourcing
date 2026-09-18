using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[Scalar]
public readonly partial record struct Name
{
	public string Value { get; }

	static partial void OnValidate(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new ArgumentException($"{nameof(Name)} cannot be empty.", nameof(value));
	}

	static partial void OnNormalize(ref string value) => value = value?.Trim()!;
}
