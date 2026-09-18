using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[Scalar]
public readonly partial record struct ProjectId
{
	public string Value { get; }

	static partial void OnValidate(string value)
	{
		if (!Guid.TryParse(value, out _))
			throw new ArgumentException("ProjectId must be a valid GUID.", nameof(value));
	}
}
