using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.Faults;

/// <summary>
/// Wraps an event store and fails chosen calls, so the paths that only run when storage misbehaves
/// can be tested.
/// </summary>
/// <remarks>
/// <para>
/// The suite otherwise covers the happy path and expected failures. The failures that actually
/// corrupt an event store are different: a write that succeeded but was never acknowledged, a call
/// that failed after committing, a retry of an operation that already happened. Those paths are
/// unreachable without something like this.
/// </para>
/// <para>
/// It decorates <see cref="IEventStoreCore{T}"/> rather than a provider, so the same tests run
/// against any store - including the in-memory one, which is what lets them live in the unit suite
/// and run on every pull request.
/// </para>
/// </remarks>
/// <typeparam name="T">The aggregate type.</typeparam>
/// <param name="inner">The store being wrapped.</param>
/// <param name="plan">Which calls to fail.</param>
public sealed class FaultInjectingEventStore<T>(IEventStoreCore<T> inner, FaultPlan plan)
	: IEventStoreCore<T>,
		IAggregateEventHistoryStoreCore<T>
	where T : class, IAggregate, new()
{
	/// <summary>The store being wrapped, for assertions about what actually reached storage.</summary>
	public IEventStoreCore<T> Inner => inner;

	/// <inheritdoc/>
	public Task<T> CreateAsync(string? aggregateId = null, CancellationToken cancellationToken = default) =>
		inner.CreateAsync(aggregateId, cancellationToken);

	/// <inheritdoc/>
	public Task<Result<T, AggregateReadFailure>> GetAsync(
		string aggregateId,
		EventStoreOperationContext? operationContext = null,
		CancellationToken cancellationToken = default
	) => InterceptAsync(FaultOperation.Get, () => inner.GetAsync(aggregateId, operationContext, cancellationToken));

	/// <inheritdoc/>
	public Task<Result<SaveOutcome<T>, AggregateSaveFailure>> SaveAsync(
		T aggregate,
		EventStoreOperationContext? operationContext = null,
		CancellationToken cancellationToken = default
	) => InterceptAsync(FaultOperation.Save, () => inner.SaveAsync(aggregate, operationContext, cancellationToken));

	/// <inheritdoc/>
	public Task<Result<SaveOutcome<T>, AggregateDeleteFailure>> DeleteAsync(
		T aggregate,
		EventStoreOperationContext? operationContext = null,
		CancellationToken cancellationToken = default
	) => InterceptAsync(FaultOperation.Delete, () => inner.DeleteAsync(aggregate, operationContext, cancellationToken));

	/// <inheritdoc/>
	public IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync(
		string aggregateId,
		long versionFrom,
		long? versionTo,
		CancellationToken cancellationToken
	)
	{
		var fault = plan.NextFor(FaultOperation.GetEventRange);
		if (fault is { Kind: FaultKind.FailBefore })
			throw fault.Build();

		return ((IAggregateEventHistoryStoreCore<T>)inner).GetEventRangeAsync(
			aggregateId,
			versionFrom,
			versionTo,
			cancellationToken
		);
	}

	/// <inheritdoc/>
	public Task<Result<T, AggregateReadFailure>> GetOrCreateAsync(
		string? aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => inner.GetOrCreateAsync(aggregateId, operationContext, cancellationToken);

	/// <inheritdoc/>
	public Task<Result<T, AggregateReadFailure>> GetAtAsync(
		string aggregateId,
		long version,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => inner.GetAtAsync(aggregateId, version, operationContext, cancellationToken);

	/// <inheritdoc/>
	public Task<Result<T, AggregateReadFailure>> GetDeletedAsync(
		string aggregateId,
		CancellationToken cancellationToken = default
	) => inner.GetDeletedAsync(aggregateId, cancellationToken);

	/// <inheritdoc/>
	public Task<Result<SaveOutcome<T>, AggregateRestoreFailure>> RestoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => inner.RestoreAsync(aggregate, operationContext, cancellationToken);

	/// <inheritdoc/>
	public IAsyncEnumerable<string> GetAggregateIdsAsync(
		bool includeDeleted,
		CancellationToken cancellationToken = default
	) => inner.GetAggregateIdsAsync(includeDeleted, cancellationToken);

	/// <inheritdoc/>
	public T FulfilRequirements(T aggregate) => inner.FulfilRequirements(aggregate);

	/// <inheritdoc/>
	public Task<AggregateExistence> ExistsAsync(string aggregateId, CancellationToken cancellationToken = default) =>
		inner.ExistsAsync(aggregateId, cancellationToken);

	async Task<TResult> InterceptAsync<TResult>(FaultOperation operation, Func<Task<TResult>> call)
	{
		var fault = plan.NextFor(operation);

		if (fault is { Kind: FaultKind.FailBefore })
			throw fault.Build();

		var result = await call();

		// FailAfter runs the real call first, so storage has already changed when the caller is told
		// it has not. That asymmetry is the whole point of the fault.
		if (fault is { Kind: FaultKind.FailAfter })
			throw fault.Build();

		return result;
	}
}
