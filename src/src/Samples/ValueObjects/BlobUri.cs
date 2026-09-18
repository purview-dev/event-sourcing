using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[Scalar]
public sealed partial record BlobUri
{
	public Uri Value { get; }

	static partial void OnValidate(Uri value)
	{
		ArgumentNullException.ThrowIfNull(value);

		if (value.IsAbsoluteUri)
			throw new ArgumentException("Only relative Uris to blob storage are valid");
	}
}
