namespace Purview.EventSourcing.Admin.Models;

/// <summary>
/// Summarizes the current state of an aggregate stream.
/// </summary>
/// <param name="AggregateType">The aggregate type name.</param>
/// <param name="AggregateId">The aggregate identifier.</param>
/// <param name="CurrentVersion">The current stream version of the aggregate.</param>
/// <param name="CreatedUtc">The UTC timestamp at which the aggregate was created.</param>
/// <param name="LastUpdatedUtc">The UTC timestamp at which the aggregate was last updated.</param>
/// <param name="IsDeleted">A value indicating whether the aggregate is soft-deleted.</param>
/// <param name="IsRestored">A value indicating whether the aggregate was restored after being soft-deleted.</param>
public sealed record AggregateSummaryResponse(
	string AggregateType,
	string AggregateId,
	long CurrentVersion,
	DateTimeOffset CreatedUtc,
	DateTimeOffset LastUpdatedUtc,
	bool IsDeleted,
	bool IsRestored
);
