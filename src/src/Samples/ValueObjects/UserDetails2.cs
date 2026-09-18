using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[ValueObject]
public sealed partial record UserDetails2(Guid Id, string DisplayName)
{
	partial void OnValidate(Guid id, string displayName)
	{
		if (id == Guid.Empty)
			throw new ArgumentException("Id must be a valid GUID.", nameof(id));

		if (string.IsNullOrWhiteSpace(displayName))
			throw new ArgumentException("DisplayName cannot be null or empty.", nameof(displayName));
	}
}
