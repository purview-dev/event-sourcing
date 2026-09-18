using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[Scalar]
public readonly partial record struct ObjectId
{
	public string Value { get; }

	static partial void OnValidate(string value)
	{
		if (!Guid.TryParse(value, out _))
			throw new ArgumentException("ObjectId must be a valid GUID.", nameof(value));
	}

	public override string ToString() => Value;
}
