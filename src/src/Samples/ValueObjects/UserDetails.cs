using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[ValueObject]
public partial record struct UserDetails(Guid Id, string? DisplayName, bool IsActive = true)
{
	public static readonly Guid LocalUserId = Guid.Parse("10000000-0000-0000-0000-000000000000");

	static partial void OnNormalize(ref Guid id, ref string? displayName, ref bool isActive)
	{
		if (!isActive)
		{
			if (string.IsNullOrWhiteSpace(displayName))
				displayName = null;
		}
	}

	readonly partial void OnValidate(Guid id, string? displayName, bool isActive)
	{
		if (id == Guid.Empty)
			throw new ArgumentException("Id must be a valid GUID.", nameof(id));

		if (isActive && string.IsNullOrWhiteSpace(displayName))
			throw new ArgumentException(
				"DisplayName cannot be null or empty when a user is active.",
				nameof(displayName)
			);
	}
}
