using System.Text.Json.Serialization;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Cryptography;

namespace Purview.EventSourcing.Aggregates.Persistence.Events;

/// <summary>
/// An event carrying personal data, used to exercise crypto-shredding.
/// </summary>
[EventContract]
public sealed record SubjectNameSetEvent : IEventDataSubject
{
	public static int SchemaVersion => 1;

	[JsonIgnore]
	public EventMetadata Metadata { get; init; }

	public string Name { get; set; } = default!;

	/// <summary>
	/// The subject the name belongs to. Serialized, because it is the key identifier replay needs.
	/// </summary>
	public string DataSubjectId { get; set; } = default!;
}
