using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.SqlServer.Events;

partial class SqlServerEventStore<T>
{
	///<inheritdoc/>
	public async Task<Result<T, AggregateReadFailure>> GetOrCreateAsync(
		string? aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		if (!string.IsNullOrWhiteSpace(aggregateId))
		{
			var existence = await ExistsAsync(aggregateId, cancellationToken);
			if (existence.DoesExist)
				return await GetAsync(aggregateId, operationContext, cancellationToken);
		}

		return await CreateAsync(aggregateId, cancellationToken);
	}
}
