using System.Data.Common;
using System.Diagnostics;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.Internal;

namespace Purview.EventSourcing;

/// <summary>
/// Coordinates saving multiple aggregates under a shared <see cref="CorrelationId"/>.
/// </summary>
/// <remarks>
/// <para>
/// This implementation automatically selects the strongest compatible transaction coordinator for the
/// enlisted stores. When all enlisted stores share the same native transactional boundary, they are
/// committed atomically through that provider-specific coordinator. Otherwise, the transaction falls
/// back to sequential <see cref="IEventStore.SaveAsync{T}(T, EventStoreOperationContext?, CancellationToken)"/> calls under a shared <see cref="CorrelationId"/>.
/// </para>
/// <para>
/// <strong>Atomicity model:</strong> when provider-native coordination is unavailable or the enlisted
/// stores do not share the same transaction boundary, previously persisted aggregates are <em>not</em>
/// rolled back. This preserves the existing logical transaction semantics for mixed-provider or
/// unsupported scenarios.
/// </para>
/// </remarks>
/// <remarks>
/// Creates a new transaction with the given correlation ID.
/// </remarks>
public sealed class EventStoreTransaction : IEventStoreTransaction
{
	readonly List<IEnlistedAggregate> _enlisted = [];
	readonly bool _useIdempotencyMarker;
	bool _committed;
	bool _disposed;

	/// <summary>
	/// Creates a new transaction with the given correlation ID.
	/// </summary>
	/// <param name="correlationId">
	/// Optional correlation ID shared by all enlisted aggregate saves. When <see langword="null"/>, the ambient
	/// <see cref="Activity"/> ID is used when available, otherwise a new <see cref="Guid"/> is generated.
	/// </param>
	public EventStoreTransaction(string? correlationId = null)
		: this(new EventStoreTransactionOptions { CorrelationId = correlationId }) { }

	/// <summary>
	/// Creates a transaction with explicit persistence requirements.
	/// </summary>
	/// <param name="options">Transaction correlation and guarantee options.</param>
	public EventStoreTransaction(EventStoreTransactionOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var resolvedCorrelationId = options.CorrelationId ?? Activity.Current?.Id;
		CorrelationId = resolvedCorrelationId ?? Guid.NewGuid().ToString();
		_useIdempotencyMarker = !string.IsNullOrWhiteSpace(resolvedCorrelationId);
		RequiredGuarantee = options.RequiredGuarantee;
	}

	/// <inheritdoc/>
	public EventStoreTransactionGuarantee RequiredGuarantee { get; }

	/// <inheritdoc/>
	public EventStoreTransactionGuarantee AvailableGuarantee =>
		CanUseNativeTransactionCoordinator()
			? EventStoreTransactionGuarantee.Atomic
			: EventStoreTransactionGuarantee.BestEffort;

	/// <inheritdoc/>
	public string CorrelationId { get; }

	/// <inheritdoc/>
	public void Enlist<T>(T aggregate, IEventStore eventStore, EventStoreOperationContext? operationContext = null)
		where T : class, IAggregate, new()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_committed)
			throw new InvalidOperationException("Cannot enlist aggregates after the transaction has been committed.");

		ArgumentNullException.ThrowIfNull(aggregate);
		ArgumentNullException.ThrowIfNull(eventStore);

		_enlisted.Add(new EnlistedAggregate<T>(aggregate, eventStore, operationContext, _useIdempotencyMarker));
	}

	/// <inheritdoc/>
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

		_enlisted.Add(new EnlistedAggregate<T>(aggregate, eventStore, operationContext, _useIdempotencyMarker));
	}

	/// <inheritdoc/>
	public async Task<TransactionResult> CommitAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_committed)
			throw new InvalidOperationException("This transaction has already been committed.");

		_committed = true;
		var availableGuarantee = AvailableGuarantee;
		if (availableGuarantee < RequiredGuarantee)
			throw new EventStoreTransactionGuaranteeException(RequiredGuarantee, availableGuarantee);

		// If no aggregates were enlisted, return an empty result.
		return _enlisted.Count == 0 ? new([])
			: availableGuarantee == EventStoreTransactionGuarantee.Atomic
				? await CommitWithNativeTransactionAsync(cancellationToken)
			: await CommitSequentiallyAsync(cancellationToken);
	}

	bool CanUseNativeTransactionCoordinator()
	{
		if (_enlisted.Count == 0)
			return false;

		string? boundaryKey = null;

		foreach (var enlisted in _enlisted)
		{
			if (string.IsNullOrWhiteSpace(enlisted.TransactionBoundaryKey))
				return false;

			if (boundaryKey is null)
			{
				boundaryKey = enlisted.TransactionBoundaryKey;
				continue;
			}

			if (!StringComparer.Ordinal.Equals(boundaryKey, enlisted.TransactionBoundaryKey))
				return false;
		}

		return boundaryKey is not null;
	}

	async Task<TransactionResult> CommitSequentiallyAsync(CancellationToken cancellationToken)
	{
		List<TransactionAggregateResult> results = new(_enlisted.Count);

		foreach (var enlisted in _enlisted)
		{
			try
			{
				var (saved, skipped, failure) = await enlisted.SaveAsync(CorrelationId, cancellationToken);
				results.Add(
					new TransactionAggregateResult(
						enlisted.Aggregate,
						saved: saved,
						skipped: skipped,
						error: null,
						failure: failure
					)
				);

				// Stop processing remaining aggregates on first failure.
				if (!saved && !skipped)
					break;
			}
#pragma warning disable CA1031
			catch (Exception ex)
#pragma warning restore CA1031
			{
				results.Add(
					new TransactionAggregateResult(enlisted.Aggregate, saved: false, skipped: false, error: ex)
				);

				// Stop on first exception — don't persist remaining aggregates.
				break;
			}
		}

		return new TransactionResult(results);
	}

	async Task<TransactionResult> CommitWithNativeTransactionAsync(CancellationToken cancellationToken)
	{
		await using var connection = _enlisted[0].CreateTransactionConnection();
		await connection.OpenAsync(cancellationToken);

		foreach (var enlisted in _enlisted)
			await enlisted.EnsureTransactionConfiguredAsync(connection, cancellationToken);

		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

		List<IProcessedSaveOperation> processed = new(_enlisted.Count);
		IEnlistedAggregate? failedEnlisted = null;
		Exception? failure = null;

		foreach (var enlisted in _enlisted)
		{
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
#pragma warning disable CA1031
			catch (Exception ex)
#pragma warning restore CA1031
			{
				failedEnlisted = enlisted;
				failure = ex;
				break;
			}
		}

		if (failedEnlisted is null)
		{
			await transaction.CommitAsync(cancellationToken);

			List<TransactionAggregateResult> committedResults = new(processed.Count);
			foreach (var operation in processed)
			{
				Exception? postCommitError = null;
				try
				{
					await operation.AfterCommitAsync(cancellationToken);
				}
#pragma warning disable CA1031
				catch (Exception ex)
#pragma warning restore CA1031
				{
					postCommitError = ex;
				}

				committedResults.Add(
					new TransactionAggregateResult(
						operation.Aggregate,
						operation.Saved,
						operation.Skipped,
						postCommitError,
						operation.Failure
					)
				);
			}

			return new TransactionResult(committedResults);
		}

		await transaction.RollbackAsync(cancellationToken);

		foreach (var operation in processed)
			await operation.AfterRollbackAsync(cancellationToken);

		List<TransactionAggregateResult> rollbackResults = new(processed.Count + 1);
		var rollbackError =
			failure
			?? new InvalidOperationException(
				"The transaction was rolled back because an enlisted aggregate could not be saved."
			);

		foreach (var operation in processed)
		{
			var isFailedOperation =
				failure is null
				&& ReferenceEquals(operation.Aggregate, failedEnlisted.Aggregate)
				&& !operation.Saved
				&& !operation.Skipped;
			var wasRolledBack = operation.Saved;

			rollbackResults.Add(
				new TransactionAggregateResult(
					operation.Aggregate,
					saved: false,
					skipped: !isFailedOperation && !wasRolledBack && operation.Skipped,
					error: isFailedOperation || operation.Skipped ? null : rollbackError,
					failure: operation.Failure
				)
			);
		}

		if (
			failure is not null
			&& processed.All(operation => !ReferenceEquals(operation.Aggregate, failedEnlisted.Aggregate))
		)
		{
			rollbackResults.Add(
				new TransactionAggregateResult(failedEnlisted.Aggregate, saved: false, skipped: false, error: failure)
			);
		}

		return new TransactionResult(rollbackResults);
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		_disposed = true;
		_enlisted.Clear();

		return ValueTask.CompletedTask;
	}

	/// <summary>Non-generic handle to an enlisted aggregate.</summary>
	internal interface IEnlistedAggregate
	{
		IAggregate Aggregate { get; }

		string? TransactionBoundaryKey { get; }

		Task<(bool saved, bool skipped, AggregateSaveFailure? failure)> SaveAsync(
			string correlationId,
			CancellationToken cancellationToken
		);

		DbConnection CreateTransactionConnection();

		Task EnsureTransactionConfiguredAsync(DbConnection connection, CancellationToken cancellationToken);

		Task<IProcessedSaveOperation> SaveInTransactionAsync(
			string correlationId,
			DbConnection connection,
			DbTransaction transaction,
			CancellationToken cancellationToken
		);
	}

	internal interface IProcessedSaveOperation
	{
		IAggregate Aggregate { get; }

		bool Saved { get; }

		bool Skipped { get; }

		AggregateSaveFailure? Failure { get; }

		Task AfterCommitAsync(CancellationToken cancellationToken);

		Task AfterRollbackAsync(CancellationToken cancellationToken);
	}

	/// <summary>Closed-generic wrapper that holds the aggregate, event store, and operation context.</summary>
	sealed class EnlistedAggregate<T> : IEnlistedAggregate
		where T : class, IAggregate, new()
	{
		readonly T _aggregate;
		readonly IEventStore? _eventStore;
		readonly IEventStoreCore<T>? _eventStoreImpl;
		readonly ITransactionalEventStore<T>? _transactionalEventStore;
		readonly EventStoreOperationContext? _operationContext;
		readonly bool _useIdempotencyMarker;

		public EnlistedAggregate(
			T aggregate,
			IEventStore eventStore,
			EventStoreOperationContext? operationContext,
			bool useIdempotencyMarker
		)
		{
			_aggregate = aggregate;
			_eventStore = eventStore;
			_eventStoreImpl = (eventStore as IEventStoreImplementationAccessor)?.GetEventStore<T>();
			_transactionalEventStore = _eventStoreImpl as ITransactionalEventStore<T>;
			_operationContext = operationContext;
			_useIdempotencyMarker = useIdempotencyMarker;
		}

		public EnlistedAggregate(
			T aggregate,
			IEventStoreCore<T> eventStore,
			EventStoreOperationContext? operationContext,
			bool useIdempotencyMarker
		)
		{
			_aggregate = aggregate;
			_eventStore = null;
			_eventStoreImpl = eventStore;
			_transactionalEventStore = eventStore as ITransactionalEventStore<T>;
			_operationContext = operationContext;
			_useIdempotencyMarker = useIdempotencyMarker;
		}

		public IAggregate Aggregate => _aggregate;

		public string? TransactionBoundaryKey => _transactionalEventStore?.TransactionBoundaryKey;

		public async Task<(bool saved, bool skipped, AggregateSaveFailure? failure)> SaveAsync(
			string correlationId,
			CancellationToken cancellationToken
		)
		{
			var baseContext = _operationContext ?? EventStoreOperationContext.DefaultContext(correlationId);
			var context = baseContext with
			{
				CorrelationId = _operationContext is null ? correlationId : baseContext.CorrelationId,
				UseIdempotencyMarker = baseContext.UseIdempotencyMarker || _useIdempotencyMarker,
			};

			var result = _eventStoreImpl is not null
				? await _eventStoreImpl.SaveAsync(_aggregate, context, cancellationToken)
				: await _eventStore!.SaveAsync(_aggregate, context, cancellationToken);

			return result.IsSuccess
				? (
					result.Value.Kind is not SaveOutcomeKind.NoChanges,
					result.Value.Kind is not SaveOutcomeKind.Written,
					null
				)
				: (false, false, result.Error);
		}

		public DbConnection CreateTransactionConnection() =>
			_transactionalEventStore?.CreateTransactionConnection()
			?? throw new InvalidOperationException(
				$"The enlisted event store '{_eventStoreImpl?.GetType().FullName ?? _eventStore?.GetType().FullName}' does not support native transactions."
			);

		public Task EnsureTransactionConfiguredAsync(DbConnection connection, CancellationToken cancellationToken) =>
			_transactionalEventStore?.EnsureTransactionConfiguredAsync(connection, cancellationToken)
			?? throw new InvalidOperationException(
				$"The enlisted event store '{_eventStoreImpl?.GetType().FullName ?? _eventStore?.GetType().FullName}' does not support native transactions."
			);

		public async Task<IProcessedSaveOperation> SaveInTransactionAsync(
			string correlationId,
			DbConnection connection,
			DbTransaction transaction,
			CancellationToken cancellationToken
		)
		{
			if (_transactionalEventStore is null)
			{
				throw new InvalidOperationException(
					$"The enlisted event store '{_eventStoreImpl?.GetType().FullName ?? _eventStore?.GetType().FullName}' does not support native transactions."
				);
			}

			var baseContext = _operationContext ?? EventStoreOperationContext.DefaultContext(correlationId);
			var context = baseContext with
			{
				CorrelationId = _operationContext is null ? correlationId : baseContext.CorrelationId,
				UseIdempotencyMarker = baseContext.UseIdempotencyMarker || _useIdempotencyMarker,
			};

			var saveOperation = await _transactionalEventStore.SaveInTransactionAsync(
				_aggregate,
				context,
				connection,
				transaction,
				cancellationToken
			);

			return new ProcessedSaveOperation<T>(_aggregate, saveOperation);
		}
	}

	sealed class ProcessedSaveOperation<T>(T aggregate, TransactionalSaveOperation<T> operation)
		: IProcessedSaveOperation
		where T : class, IAggregate, new()
	{
		public IAggregate Aggregate => aggregate;

		public bool Saved => operation.Saved;

		public bool Skipped => operation.Skipped;

		public AggregateSaveFailure? Failure => operation.Result.IsFailure ? operation.Result.Error : null;

		public Task AfterCommitAsync(CancellationToken cancellationToken) =>
			operation.AfterCommitAsync(cancellationToken);

		public Task AfterRollbackAsync(CancellationToken cancellationToken) =>
			operation.AfterRollbackAsync(cancellationToken);
	}
}
