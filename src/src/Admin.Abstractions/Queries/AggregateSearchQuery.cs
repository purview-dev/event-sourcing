namespace Purview.EventSourcing.Admin.Queries;

/// <summary>
/// Defines the criteria used to search for aggregates in the admin portal.
/// </summary>
/// <param name="AggregateType">The aggregate type name to filter by, or <see langword="null"/> to match all types.</param>
/// <param name="AggregateId">The aggregate identifier to filter by, or <see langword="null"/> to match all identifiers.</param>
/// <param name="FromUtc">The inclusive lower bound of the last-updated timestamp (UTC) to filter by, or <see langword="null"/> to ignore the bound.</param>
/// <param name="ToUtc">The inclusive upper bound of the last-updated timestamp (UTC) to filter by, or <see langword="null"/> to ignore the bound.</param>
/// <param name="IsDeleted">When set, filters to aggregates that are or are not soft-deleted.</param>
/// <param name="IsRestored">When set, filters to aggregates that are or are not restored after deletion.</param>
/// <param name="Page">The one-based page number to return.</param>
/// <param name="PageSize">The maximum number of aggregates to return per page.</param>
/// <param name="Sort">The sort expression, for example <c>"LastUpdatedUtc desc"</c>.</param>
public sealed record AggregateSearchQuery(
	string? AggregateType,
	string? AggregateId,
	DateTimeOffset? FromUtc,
	DateTimeOffset? ToUtc,
	bool? IsDeleted,
	bool? IsRestored,
	int Page = 1,
	int PageSize = 50,
	string Sort = "LastUpdatedUtc desc"
);
