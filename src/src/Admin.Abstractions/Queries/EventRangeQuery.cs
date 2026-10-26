namespace Purview.EventSourcing.Admin.Queries;

/// <summary>
/// Defines a query over a range of events within a single aggregate stream.
/// </summary>
/// <param name="VersionFrom">The inclusive lower bound of the stream version to query, or <see langword="null"/> to start from the beginning.</param>
/// <param name="VersionTo">The inclusive upper bound of the stream version to query, or <see langword="null"/> to include the latest events.</param>
/// <param name="TimeFromUtc">The inclusive lower bound of the event timestamp (UTC) to query, or <see langword="null"/> to ignore the time bound.</param>
/// <param name="TimeToUtc">The inclusive upper bound of the event timestamp (UTC) to query, or <see langword="null"/> to ignore the time bound.</param>
/// <param name="Page">The one-based page number to return.</param>
/// <param name="PageSize">The maximum number of events to return per page.</param>
/// <param name="Sort">The sort expression, for example <c>"Version asc"</c>.</param>
public sealed record EventRangeQuery(
	long? VersionFrom,
	long? VersionTo,
	DateTimeOffset? TimeFromUtc,
	DateTimeOffset? TimeToUtc,
	int Page = 1,
	int PageSize = 50,
	string Sort = "Version asc"
);
