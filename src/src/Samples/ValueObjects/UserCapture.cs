using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[ValueObject]
public partial record struct UserCapture(UserDetails User, DateTimeOffset OccurredAt)
{
	public readonly bool IsEssentialChange(UserCapture userDetails) =>
		OccurredAt != userDetails.OccurredAt || User.Id != userDetails.User.Id;
}
