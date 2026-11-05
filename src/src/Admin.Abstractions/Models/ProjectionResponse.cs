using System.Text.Json;

namespace Purview.EventSourcing.Admin.Models;

/// <summary>
/// Represents the projected state of an aggregate at a point in time or version.
/// </summary>
/// <param name="AggregateType">The aggregate type name.</param>
/// <param name="AggregateId">The aggregate identifier.</param>
/// <param name="ProjectedVersion">The stream version the projection was built from.</param>
/// <param name="ProjectedAtUtc">The UTC timestamp of the latest event applied, when known.</param>
/// <param name="State">The projected aggregate state as raw JSON.</param>
/// <param name="Provenance">Information about how the projection was produced.</param>
public sealed record ProjectionResponse(
	string AggregateType,
	string AggregateId,
	long ProjectedVersion,
	DateTimeOffset? ProjectedAtUtc,
	JsonElement State,
	ProjectionProvenance Provenance
);
