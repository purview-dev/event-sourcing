using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Purview.EventSourcing.Admin.Models;

namespace Purview.EventSourcing.Admin.MongoDB;

/// <summary>
/// The stored shape of an <see cref="AdminAuditEntry"/>.
/// </summary>
/// <remarks>
/// A deliberate document type rather than serialising <see cref="AdminAuditEntry"/> directly:
/// the stored shape of an audit trail outlives the code that wrote it, and an entry record that
/// gains a property should not silently change what a five-year-old document is expected to contain.
/// <see cref="Feature"/> is stored as its name for the same reason - an enum ordinal would be
/// rewritten in meaning by inserting a member.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class MongoDBAdminAuditDocument
{
	/// <summary>The document identifier.</summary>
	[BsonId]
	public ObjectId Id { get; set; }

	/// <summary>When the operation occurred.</summary>
	public DateTime TimestampUtc { get; set; }

	/// <summary>The feature exercised, stored by name.</summary>
	public string Feature { get; set; } = string.Empty;

	/// <summary>The action performed.</summary>
	public string Action { get; set; } = string.Empty;

	/// <summary>The authenticated principal, when known.</summary>
	public string? Principal { get; set; }

	/// <summary>The tenant the operation acted on, or <see langword="null"/> when unowned.</summary>
	public string? TenantId { get; set; }

	/// <summary>The operation target, when applicable.</summary>
	public string? Target { get; set; }

	/// <summary>Whether the operation succeeded.</summary>
	public bool Succeeded { get; set; }

	/// <summary>Optional details, for example a refusal reason.</summary>
	public string? Details { get; set; }

	/// <summary>Correlates the entry with the request and the events it produced.</summary>
	public string? CorrelationId { get; set; }

	/// <summary>Reserved so hash chaining can be added later without rewriting stored documents.</summary>
	public byte[]? PreviousHash { get; set; }

	internal static MongoDBAdminAuditDocument From(AdminAuditEntry entry) =>
		new()
		{
			TimestampUtc = entry.TimestampUtc.UtcDateTime,
			Feature = entry.Feature.ToString(),
			Action = entry.Action,
			Principal = entry.Principal,
			TenantId = entry.TenantId,
			Target = entry.Target,
			Succeeded = entry.Succeeded,
			Details = entry.Details,
			CorrelationId = entry.CorrelationId,
		};

	internal AdminAuditEntry ToEntry() =>
		new(
			new DateTimeOffset(DateTime.SpecifyKind(TimestampUtc, DateTimeKind.Utc)),
			Enum.TryParse<AdminFeature>(Feature, out var feature) ? feature : default,
			Action,
			Principal,
			Target,
			Succeeded,
			Details
		)
		{
			TenantId = TenantId,
			CorrelationId = CorrelationId,
		};
}
