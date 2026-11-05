using System.Diagnostics;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.MongoDB;

partial class MongoDBEventStore<T>
{
	///<inheritdoc/>
	public async Task<Result<T, AggregateReadFailure>> GetAtAsync(
		string aggregateId,
		long version,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId, nameof(aggregateId));

		operationContext ??= EventStoreOperationContext.DefaultContext();

		using var activity = _eventStoreTelemetry.GetAggregateAtVersion(aggregateId, version, _aggregateTypeFullName);
		_eventStoreTelemetry.GetAggregateAtSpecificVersionStart(aggregateId, version, _aggregateTypeFullName);
		var getStopwatch = Stopwatch.StartNew();
		try
		{
			var streamVersion = await GetStreamVersionAsync(aggregateId, true, cancellationToken);
			if (streamVersion == null)
				return NotFound(aggregateId);

			if (!ReturnAggregate(streamVersion.IsDeleted, operationContext))
				return Deleted(aggregateId);

			T aggregate = new() { Details = { Id = aggregateId } };

			await GetAndApplyEventsAsync(aggregate, streamVersion, version, cancellationToken);

			// Make sure we lock the aggregate to prevent saving/ modifications etc.
			aggregate.Details.Locked = true;

			return FulfilRequirements(aggregate);
		}
		catch (Exception ex)
		{
			_eventStoreTelemetry.GetAggregateAtSpecificVersionFailed(aggregateId, _aggregateTypeFullName, version, ex);
			throw;
		}
		finally
		{
			getStopwatch.Stop();
			_eventStoreTelemetry.GetAggregateAtSpecificVersionComplete(
				aggregateId,
				_aggregateTypeFullName,
				version,
				getStopwatch.ElapsedMilliseconds
			);
		}
	}
}
