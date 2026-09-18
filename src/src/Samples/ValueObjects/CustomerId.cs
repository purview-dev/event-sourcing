using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[Scalar]
public readonly partial record struct CustomerId
{
	public Guid Value { get; }

	CustomerId(Guid value) => Value = value;

	static partial void OnValidate(Guid value)
	{
		if (value == Guid.Empty)
			throw new ArgumentException("Customer id cannot be empty.", nameof(value));
	}
}
