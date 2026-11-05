using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Internal;

namespace Purview.EventSourcing.SqlServer.Events;

/// <summary>
/// A SQL Server transaction coordinator that supports event-store aggregate saves and
/// additional SQL/EF work inside the same SQL transaction.
/// </summary>
public interface ISqlServerEventStoreTransaction : IEventStoreTransaction
{
	/// <summary>
	/// Enlists additional SQL work to execute inside the transaction.
	/// </summary>
	void Enlist(Func<SqlConnection, SqlTransaction, CancellationToken, Task> operation);

	/// <summary>
	/// Enlists an EF Core unit of work to execute inside the transaction.
	/// </summary>
	void Enlist<TDbContext>(
		Func<SqlConnection, TDbContext> dbContextFactory,
		Func<TDbContext, CancellationToken, Task> operation
	)
		where TDbContext : DbContext;
}

sealed class SqlServerEventStoreTransaction(string? correlationId = null) : ISqlServerEventStoreTransaction
{
	readonly List<IEnlistedAggregate> _enlisted = [];
	readonly List<Func<SqlConnection, SqlTransaction, CancellationToken, Task>> _sqlOperations = [];
	readonly bool _useIdempotencyMarker = !string.IsNullOrWhiteSpace(correlationId ?? Activity.Current?.Id);
	string? _transactionBoundaryKey;

	bool _committed;
	bool _disposed;

	public string CorrelationId { get; } = correlationId ?? Activity.Current?.Id ?? Guid.NewGuid().ToString();

	public EventStoreTransactionGuarantee RequiredGuarantee => EventStoreTransactionGuarantee.Atomic;

	public EventStoreTransactionGuarantee AvailableGuarantee => EventStoreTransactionGuarantee.Atomic;

	public void Enlist<T>(T aggregate, IEventStore eventStore, EventStoreOperationContext? operationContext = null)
		where T : class, IAggregate, new()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_committed)
			throw new InvalidOperationException("Cannot enlist aggregates after the transaction has been committed.");

		ArgumentNullException.ThrowIfNull(aggregate);
		ArgumentNullException.ThrowIfNull(eventStore);

		if (
			(eventStore as IEventStoreImplementationAccessor)?.GetEventStore<T>()
			is not ITransactionalEventStore<T> transactionalEventStore
		)
		{
			throw new InvalidOperationException(
				$"The enlisted event store '{eventStore.GetType().FullName}' does not support atomic SQL Server transactions."
			);
		}

		EnsureTransactionBoundary(transactionalEventStore.TransactionBoundaryKey, eventStore.GetType().FullName);
		_enlisted.Add(
			new EnlistedAggregate<T>(aggregate, transactionalEventStore, operationContext, _useIdempotencyMarker)
		);
	}

	public void Enlist<T>(
		T aggregate,
		IEventStoreCore<T> eventStore,
		EventStoreOperationContext? operationContext = null
	)
		where T : class, IAggregate, new()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_committed)
			throw new InvalidOperationException("Cannot enlist aggregates after the transaction has been committed.");

		ArgumentNullException.ThrowIfNull(aggregate);
		ArgumentNullException.ThrowIfNull(eventStore);

		if (eventStore is not ITransactionalEventStore<T> transactionalEventStore)
		{
			throw new InvalidOperationException(
				$"The enlisted event store '{eventStore.GetType().FullName}' does not support atomic SQL Server transactions."
			);
		}

		EnsureTransactionBoundary(transactionalEventStore.TransactionBoundaryKey, eventStore.GetType().FullName);
		_enlisted.Add(
			new EnlistedAggregate<T>(aggregate, transactionalEventStore, operationContext, _useIdempotencyMarker)
		);
	}

	public void Enlist(Func<SqlConnection, SqlTransaction, CancellationToken, Task> operation)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_committed)
			throw new InvalidOperationException("Cannot enlist operations after the transaction has been committed.");

		ArgumentNullException.ThrowIfNull(operation);
		_sqlOperations.Add(operation);
	}

	public void Enlist<TDbContext>(
		Func<SqlConnection, TDbContext> dbContextFactory,
		Func<TDbContext, CancellationToken, Task> operation
	)
		where TDbContext : DbContext
	{
		ArgumentNullException.ThrowIfNull(dbContextFactory);
		ArgumentNullException.ThrowIfNull(operation);

		Enlist(
			async (connection, transaction, cancellationToken) =>
			{
				await using var dbContext = dbContextFactory(connection);
				await dbContext.Database.UseTransactionAsync(transaction, cancellationToken);
				await operation(dbContext, cancellationToken);
			}
		);
	}

	public async Task<TransactionResult> CommitAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_committed)
			throw new InvalidOperationException("This transaction has already been committed.");

		_committed = true;

		if (_enlisted.Count == 0)
		{
			return _sqlOperations.Count == 0
				? new TransactionResult([])
				: throw new InvalidOperationException(
					"A SQL Server transaction requires at least one enlisted aggregate to establish the transactional connection boundary."
				);
		}

		await using var connection = _enlisted[0].CreateTransactionConnection();
		await PrepareTransactionConnectionAsync(connection, cancellationToken);

		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

		List<IProcessedSaveOperation> processed = [with(_enlisted.Count)];
		var (failedEnlisted, failure) = await ExecuteEnlistedSavesAsync(
			connection,
			transaction,
			processed,
			cancellationToken
		);

		if (failedEnlisted is null && failure is null)
			failure = await ExecuteAdditionalSqlOperationsAsync(connection, transaction, cancellationToken);

		if (failedEnlisted is null && failure is null)
			return await CommitAndNotifyAsync(transaction, processed, cancellationToken);

		// Rollback the transaction and build a result indicating which aggregate failed to save.
		return await RollbackAndBuildResultAsync(transaction, processed, failedEnlisted, failure, cancellationToken);
	}

	async Task PrepareTransactionConnectionAsync(DbConnection connection, CancellationToken cancellationToken)
	{
		await connection.OpenAsync(cancellationToken);

		foreach (var enlisted in _enlisted)
			await enlisted.EnsureTransactionConfiguredAsync(connection, cancellationToken);
	}

	async Task<(IEnlistedAggregate? failedEnlisted, Exception? failure)> ExecuteEnlistedSavesAsync(
		DbConnection connection,
		DbTransaction transaction,
		List<IProcessedSaveOperation> processed,
		CancellationToken cancellationToken
	)
	{
		IEnlistedAggregate? failedEnlisted = null;
		Exception? failure = null;

		foreach (var enlisted in _enlisted)
		{
#pragma warning disable CA1031
			try
			{
				var operation = await enlisted.SaveInTransactionAsync(
					CorrelationId,
					connection,
					transaction,
					cancellationToken
				);

				processed.Add(operation);

				if (!operation.Saved && !operation.Skipped)
				{
					failedEnlisted = enlisted;
					break;
				}
			}
			catch (Exception ex)
			{
				failedEnlisted = enlisted;
				failure = ex;
				break;
			}
#pragma warning restore CA1031
		}

		return (failedEnlisted, failure);
	}

	async Task<Exception?> ExecuteAdditionalSqlOperationsAsync(
		DbConnection connection,
		DbTransaction transaction,
		CancellationToken cancellationToken
	)
	{
		var sqlConnection = GetSqlConnection(connection);
		var sqlTransaction = GetSqlTransaction(transaction);
		Exception? failure = null;

		foreach (var operation in _sqlOperations)
		{
#pragma warning disable CA1031
			try
			{
				await operation(sqlConnection, sqlTransaction, cancellationToken);
			}
			catch (Exception ex)
			{
				failure = ex;
				break;
			}
#pragma warning restore CA1031
		}

		return failure;
	}

	static async Task<TransactionResult> CommitAndNotifyAsync(
		DbTransaction transaction,
		List<IProcessedSaveOperation> processed,
		CancellationToken cancellationToken
	)
	{
		await transaction.CommitAsync(cancellationToken);

		List<TransactionAggregateResult> committedResults = [with(processed.Count)];
		foreach (var operation in processed)
		{
			Exception? postCommitError = null;
#pragma warning disable CA1031
			try
			{
				await operation.AfterCommitAsync(cancellationToken);
			}
			catch (Exception ex)
			{
				postCommitError = ex;
			}
#pragma warning restore CA1031

			committedResults.Add(new(operation.Aggregate, operation.Saved, operation.Skipped, postCommitError));
		}

		return new TransactionResult(committedResults);
	}

	static async Task<TransactionResult> RollbackAndBuildResultAsync(
		DbTransaction transaction,
		List<IProcessedSaveOperation> processed,
		IEnlistedAggregate? failedEnlisted,
		Exception? failure,
		CancellationToken cancellationToken
	)
	{
		await transaction.RollbackAsync(cancellationToken);

		foreach (var operation in processed)
			await operation.AfterRollbackAsync(cancellationToken);

		return BuildRollbackResult(processed, failedEnlisted, failure);
	}

	static TransactionResult BuildRollbackResult(
		List<IProcessedSaveOperation> processed,
		IEnlistedAggregate? failedEnlisted,
		Exception? failure
	)
	{
		List<TransactionAggregateResult> rollbackResults = new(processed.Count + 1);
		var rollbackError =
			failure
			?? new InvalidOperationException(
				"The transaction was rolled back because an enlisted aggregate could not be saved."
			);

		foreach (var operation in processed)
		{
			var isFailedOperation =
				failedEnlisted is not null
				&& failure is null
				&& ReferenceEquals(operation.Aggregate, failedEnlisted.Aggregate)
				&& !operation.Saved
				&& !operation.Skipped;
			var wasRolledBack = operation.Saved;

			rollbackResults.Add(
				new TransactionAggregateResult(
					operation.Aggregate,
					saved: false,
					skipped: !isFailedOperation && !wasRolledBack && operation.Skipped,
					error: isFailedOperation || operation.Skipped ? null : rollbackError
				)
			);
		}

		if (
			failedEnlisted is not null
			&& failure is not null
			&& processed.All(operation => !ReferenceEquals(operation.Aggregate, failedEnlisted.Aggregate))
		)
		{
			rollbackResults.Add(
				new TransactionAggregateResult(failedEnlisted.Aggregate, saved: false, skipped: false, error: failure)
			);
		}

		return new TransactionResult(rollbackResults);
	}

	public ValueTask DisposeAsync()
	{
		_disposed = true;
		_enlisted.Clear();
		_sqlOperations.Clear();

		return ValueTask.CompletedTask;
	}

	interface IEnlistedAggregate
	{
		IAggregate Aggregate { get; }
		DbConnection CreateTransactionConnection();
		Task EnsureTransactionConfiguredAsync(DbConnection connection, CancellationToken cancellationToken);
		Task<IProcessedSaveOperation> SaveInTransactionAsync(
			string correlationId,
			DbConnection connection,
			DbTransaction transaction,
			CancellationToken cancellationToken
		);
	}

	interface IProcessedSaveOperation
	{
		IAggregate Aggregate { get; }
		bool Saved { get; }
		bool Skipped { get; }
		Task AfterCommitAsync(CancellationToken cancellationToken);
		Task AfterRollbackAsync(CancellationToken cancellationToken);
	}

	sealed class EnlistedAggregate<T>(
		T aggregate,
		ITransactionalEventStore<T> eventStore,
		EventStoreOperationContext? operationContext,
		bool useIdempotencyMarker
	) : IEnlistedAggregate
		where T : class, IAggregate, new()
	{
		public IAggregate Aggregate => aggregate;

		public DbConnection CreateTransactionConnection() => eventStore.CreateTransactionConnection();

		public Task EnsureTransactionConfiguredAsync(DbConnection connection, CancellationToken cancellationToken) =>
			eventStore.EnsureTransactionConfiguredAsync(connection, cancellationToken);

		public async Task<IProcessedSaveOperation> SaveInTransactionAsync(
			string correlationId,
			DbConnection connection,
			DbTransaction transaction,
			CancellationToken cancellationToken
		)
		{
			var baseContext = operationContext ?? EventStoreOperationContext.DefaultContext(correlationId);
			var context = baseContext with
			{
				CorrelationId = operationContext is null ? correlationId : baseContext.CorrelationId,
				UseIdempotencyMarker = baseContext.UseIdempotencyMarker || useIdempotencyMarker,
			};

			var saveOperation = await eventStore.SaveInTransactionAsync(
				aggregate,
				context,
				connection,
				transaction,
				cancellationToken
			);

			return new ProcessedSaveOperation<T>(aggregate, saveOperation);
		}
	}

	sealed class ProcessedSaveOperation<T>(T aggregate, TransactionalSaveOperation<T> operation)
		: IProcessedSaveOperation
		where T : class, IAggregate, new()
	{
		public IAggregate Aggregate => aggregate;
		public bool Saved => operation.Saved;
		public bool Skipped => operation.Skipped;

		public Task AfterCommitAsync(CancellationToken cancellationToken) =>
			operation.AfterCommitAsync(cancellationToken);

		public Task AfterRollbackAsync(CancellationToken cancellationToken) =>
			operation.AfterRollbackAsync(cancellationToken);
	}

	static SqlConnection GetSqlConnection(DbConnection connection) =>
		connection as SqlConnection
		?? throw new InvalidOperationException("SQL Server transactions require a SqlConnection.");

	static SqlTransaction GetSqlTransaction(DbTransaction transaction) =>
		transaction as SqlTransaction
		?? throw new InvalidOperationException("SQL Server transactions require a SqlTransaction.");

	void EnsureTransactionBoundary(string boundaryKey, string? eventStoreType)
	{
		if (string.IsNullOrWhiteSpace(boundaryKey))
		{
			throw new InvalidOperationException(
				$"The enlisted event store '{eventStoreType}' did not provide a SQL transaction boundary."
			);
		}

		if (_transactionBoundaryKey is null)
		{
			_transactionBoundaryKey = boundaryKey;
			return;
		}

		if (!StringComparer.Ordinal.Equals(_transactionBoundaryKey, boundaryKey))
		{
			throw new InvalidOperationException(
				"All enlisted SQL Server event stores must share the same transaction boundary."
			);
		}
	}
}
