using System.Data.Common;
using Npgsql;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.Internal;

namespace Purview.EventSourcing.Postgres.Snapshot;

partial class PostgresSnapshotEventStore<T>
{
	///<inheritdoc/>
	public Task<T> CreateAsync(string? aggregateId = null, CancellationToken cancellationToken = default) =>
		_eventStore.CreateAsync(aggregateId, cancellationToken);

	///<inheritdoc/>
	public Task<T?> GetOrCreateAsync(
		string? aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.GetOrCreateAsync(aggregateId, operationContext, cancellationToken);

	///<inheritdoc/>
	public Task<T?> GetAsync(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.GetAsync(aggregateId, operationContext, cancellationToken);

	///<inheritdoc/>
	public Task<T?> GetAtAsync(
		string aggregateId,
		int version,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _eventStore.GetAtAsync(aggregateId, version, operationContext, cancellationToken);

	///<inheritdoc/>
	public async Task<SaveResult<T>> SaveAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));
		var eventsApplied = aggregate.GetUnsavedEvents().Count;

		var result = await _eventStore.SaveAsync(aggregate, operationContext, cancellationToken);
		if (
			result
			&& !result.Skipped
			&& SnapshotStrategyResolver.ShouldSnapshot(
				aggregate,
				eventsApplied,
				operationContext,
				_snapshotStrategy,
				_snapshotStrategySelector
			)
		)
			await SnapshotAsync(aggregate, cancellationToken);

		return result;
	}

	string ITransactionalEventStore<T>.TransactionBoundaryKey =>
		_eventStore is ITransactionalEventStore<T> transactionalEventStore
			? transactionalEventStore.TransactionBoundaryKey
			: string.Empty;

	DbConnection ITransactionalEventStore<T>.CreateTransactionConnection()
	{
		return _eventStore is ITransactionalEventStore<T> transactionalEventStore
			? transactionalEventStore.CreateTransactionConnection()
			: new NpgsqlConnection(_sqlServerEventStoreOptions.Value.ConnectionString);
	}

	async Task ITransactionalEventStore<T>.EnsureTransactionConfiguredAsync(
		DbConnection connection,
		CancellationToken cancellationToken
	)
	{
		if (_eventStore is not ITransactionalEventStore<T> transactionalEventStore)
			throw new InvalidOperationException("The inner event store does not support transactional saves.");

		await transactionalEventStore.EnsureTransactionConfiguredAsync(connection, cancellationToken);
		await _sqlServerClient.EnsureTableExistsAsync(GetNpgsqlConnection(connection), cancellationToken);
	}

	async Task<TransactionalSaveOperation<T>> ITransactionalEventStore<T>.SaveInTransactionAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		DbConnection connection,
		DbTransaction transaction,
		CancellationToken cancellationToken
	)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		if (_eventStore is not ITransactionalEventStore<T> transactionalEventStore)
			throw new InvalidOperationException("The inner event store does not support transactional saves.");

		var eventsApplied = aggregate.GetUnsavedEvents().Count;
		var innerOperation = await transactionalEventStore.SaveInTransactionAsync(
			aggregate,
			operationContext,
			connection,
			transaction,
			cancellationToken
		);

		try
		{
			if (
				innerOperation.Result.Saved
				&& !innerOperation.Result.Skipped
				&& SnapshotStrategyResolver.ShouldSnapshot(
					aggregate,
					eventsApplied,
					operationContext,
					_snapshotStrategy,
					_snapshotStrategySelector
				)
			)
			{
				// A query-snapshot write failure must NOT roll back the event commit: snapshots are
				// replaceable read models that self-heal from the event stream on the next read.
				try
				{
					var snapshotSaved = await _sqlServerClient.UpsertAsync(
						aggregate,
						aggregate.Details.Id,
						GetAggregateTypeName(),
						GetNpgsqlConnection(connection),
						GetNpgsqlTransaction(transaction),
						cancellationToken
					);

					if (!snapshotSaved)
						_telemetry.SnapshotSaveFailed(
							aggregate.Details.Id,
							_aggregateName,
							new InvalidOperationException("Failed to persist the PostgreSQL query snapshot.")
						);
				}
#pragma warning disable CA1031
				catch (Exception ex)
				{
					_telemetry.SnapshotSaveFailed(aggregate.Details.Id, _aggregateName, ex);
				}
#pragma warning restore CA1031
			}

			return new TransactionalSaveOperation<T>(
				innerOperation.Result,
				innerOperation.AfterCommitAsync,
				innerOperation.AfterRollbackAsync
			);
		}
		catch
		{
			await innerOperation.AfterRollbackAsync(cancellationToken);
			throw;
		}
	}

	///<inheritdoc/>
	public Task<bool> IsDeletedAsync(string aggregateId, CancellationToken cancellationToken = default) =>
		_eventStore.IsDeletedAsync(aggregateId, cancellationToken);

	///<inheritdoc/>
	public Task<T?> GetDeletedAsync(string aggregateId, CancellationToken cancellationToken = default) =>
		_eventStore.GetDeletedAsync(aggregateId, cancellationToken);

	///<inheritdoc/>
	public async Task<bool> DeleteAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		_telemetry.SnapshotDeleteStart(aggregate.Details.Id, _aggregateName);
		using var activity = _telemetry.SnapshotDelete(aggregate.Details.Id, _aggregateName);

		try
		{
			var result = await _eventStore.DeleteAsync(aggregate, operationContext, cancellationToken);
			if (result)
			{
				await _sqlServerClient.DeleteAsync(aggregate.Details.Id, cancellationToken);
				_telemetry.SnapshotDeleteComplete(aggregate.Details.Id, _aggregateName);
			}

			return result;
		}
		catch (Exception ex)
		{
			_telemetry.SnapshotDeleteFailed(aggregate.Details.Id, _aggregateName, ex);
			throw;
		}
	}

	///<inheritdoc/>
	public async Task<bool> RestoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		var result = await _eventStore.RestoreAsync(aggregate, operationContext, cancellationToken);
		if (result)
			await SnapshotAsync(aggregate, cancellationToken);

		return result;
	}

	///<inheritdoc/>
	public IAsyncEnumerable<string> GetAggregateIdsAsync(
		bool includeDeleted,
		CancellationToken cancellationToken = default
	) => _eventStore.GetAggregateIdsAsync(includeDeleted, cancellationToken);

	///<inheritdoc/>
	public Task<ExistsState> ExistsAsync(string aggregateId, CancellationToken cancellationToken = default) =>
		_eventStore.ExistsAsync(aggregateId, cancellationToken);

	///<inheritdoc/>
	public T FulfilRequirements(T aggregate) => _eventStore.FulfilRequirements(aggregate);

	///<inheritdoc/>
	public IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync(
		string aggregateId,
		int versionFrom,
		int? versionTo,
		CancellationToken cancellationToken
	) => _eventStore.GetEventRangeAsync(aggregateId, versionFrom, versionTo, cancellationToken);

	static NpgsqlConnection GetNpgsqlConnection(DbConnection connection) =>
		connection as NpgsqlConnection
		?? throw new InvalidOperationException("PostgreSQL transactions require a NpgsqlConnection.");

	static NpgsqlTransaction GetNpgsqlTransaction(DbTransaction transaction) =>
		transaction as NpgsqlTransaction
		?? throw new InvalidOperationException("PostgreSQL transactions require a NpgsqlTransaction.");
}
