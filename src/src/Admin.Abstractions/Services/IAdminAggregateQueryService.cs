using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Queries;

namespace Purview.EventSourcing.Admin.Services;

/// <summary>
/// Queries aggregates for the admin portal, providing search and detail retrieval.
/// </summary>
public interface IAdminAggregateQueryService
{
	/// <summary>
	/// Searches for aggregates matching the specified criteria.
	/// </summary>
	/// <param name="query">The search criteria, including filters and paging.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>A paged collection of aggregate summaries matching the search criteria.</returns>
	Task<PagedResult<AggregateSummaryResponse>> SearchAsync(
		AggregateSearchQuery query,
		CancellationToken cancellationToken
	);

	/// <summary>
	/// Gets a single aggregate summary by aggregate type and identifier.
	/// </summary>
	/// <param name="aggregateType">The aggregate type name.</param>
	/// <param name="aggregateId">The aggregate identifier.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>The aggregate summary, or <see langword="null"/> if the aggregate does not exist.</returns>
	Task<AggregateSummaryResponse?> GetAsync(
		string aggregateType,
		string aggregateId,
		CancellationToken cancellationToken
	);
}
