using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.AzureStorage;

partial class TableEventStore<T>
{
	///<inheritdoc/>
	public async Task<Result<T, AggregateReadFailure>> GetDeletedAsync(
		string aggregateId,
		CancellationToken cancellationToken = default
	)
	{
		var result = await GetCoreAsync(
			aggregateId,
			new() { SnapshotCacheMode = SnapshotCachingOptions.None, DeleteMode = DeleteHandlingMode.ReturnsAggregate },
			cancellationToken
		);
		if (result.IsFailure)
			return result;

		var aggregate = result.Value;

		return aggregate.Details.IsDeleted ? FulfilRequirements(aggregate) : NotDeleted(aggregateId);
	}
}
