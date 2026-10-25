using Purview.EventSourcing.Admin.Failures;
using Purview.EventSourcing.Admin.Models;
using Purview.Results;

namespace Purview.EventSourcing.Admin.Services;

/// <summary>
/// Projects aggregate state for the admin portal at a specific version or point in time.
/// </summary>
public interface IAdminProjectionService
{
	/// <summary>
	/// Projects the aggregate state as of the specified stream version.
	/// </summary>
	/// <param name="aggregateType">The aggregate type name.</param>
	/// <param name="aggregateId">The aggregate identifier.</param>
	/// <param name="targetVersion">The stream version up to and including which events are applied.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>
	/// The projected aggregate state, or <see cref="AdminAggregateNotFound"/> when the stream holds no events
	/// up to that version.
	/// </returns>
	Task<Result<ProjectionResponse, AdminQueryFailure>> ProjectAtVersionAsync(
		string aggregateType,
		string aggregateId,
		long targetVersion,
		CancellationToken cancellationToken
	);

	/// <summary>
	/// Projects the aggregate state as of the specified UTC timestamp.
	/// </summary>
	/// <param name="aggregateType">The aggregate type name.</param>
	/// <param name="aggregateId">The aggregate identifier.</param>
	/// <param name="targetUtc">The UTC timestamp up to and including which events are applied.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>
	/// The projected aggregate state, or <see cref="AdminAggregateNotFound"/> when the stream holds no events
	/// at or before that time.
	/// </returns>
	Task<Result<ProjectionResponse, AdminQueryFailure>> ProjectAtTimeAsync(
		string aggregateType,
		string aggregateId,
		DateTimeOffset targetUtc,
		CancellationToken cancellationToken
	);
}
