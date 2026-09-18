using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[Scalar]
public readonly partial record struct GuidObjectId
{
	public Guid Value { get; }

	static partial void OnValidate(Guid value)
	{
		if (value == Guid.Empty)
			throw new ArgumentException("GuidObjectId must be a non-empty GUID.", nameof(value));
	}

	public override string ToString() => Value.ToString();
}
