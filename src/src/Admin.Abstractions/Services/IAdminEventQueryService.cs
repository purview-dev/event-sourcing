using Purview.EventSourcing.Admin.Failures;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Queries;
using Purview.Results;

namespace Purview.EventSourcing.Admin.Services;

/// <summary>
/// Queries event streams for the admin portal.
/// </summary>
public interface IAdminEventQueryService
{
	/// <summary>
	/// Gets a paged range of events for an aggregate stream.
	/// </summary>
	/// <param name="aggregateType">The aggregate type name.</param>
	/// <param name="aggregateId">The aggregate identifier.</param>
	/// <param name="query">The event range filter and paging criteria.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>
	/// A paged collection of event envelopes for the stream, or <see cref="AdminAggregateNotFound"/> when the
	/// stream does not exist.
	/// </returns>
	Task<Result<PagedResult<EventEnvelopeResponse>, AdminQueryFailure>> GetRangeAsync(
		string aggregateType,
		string aggregateId,
		EventRangeQuery query,
		CancellationToken cancellationToken
	);
}
