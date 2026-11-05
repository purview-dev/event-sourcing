using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.Exceptions;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.Internal;
using Purview.EventSourcing.Validation;
using Purview.Results;

namespace Purview.EventSourcing.SqlServer.Events;

partial class SqlServerEventStore<T>
{
	///<inheritdoc/>
	[DebuggerStepThrough]
	public async Task<Result<SaveOutcome<T>, AggregateSaveFailure>> SaveAsync(
		[NotNull] T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		var saveStopwatch = Stopwatch.StartNew();
		var operation = await SaveCoreAsync(aggregate, operationContext, null, null, cancellationToken);
		await operation.AfterCommitAsync(cancellationToken);

		saveStopwatch.Stop();
		_metrics.RecordSave(saveStopwatch.Elapsed.TotalMilliseconds, _aggregateTypeShortName, operation.Result);

		return operation.Result;
	}

	string ITransactionalEventStore<T>.TransactionBoundaryKey =>
		new SqlConnectionStringBuilder(_eventStoreOptions.Value.ConnectionString).ConnectionString;

	DbConnection ITransactionalEventStore<T>.CreateTransactionConnection() =>
		new SqlConnection(_eventStoreOptions.Value.ConnectionString);

	Task ITransactionalEventStore<T>.EnsureTransactionConfiguredAsync(
		DbConnection connection,
		CancellationToken cancellationToken
	) => _client.EnsureTableExistsAsync(GetSqlConnection(connection), cancellationToken);

	Task<TransactionalSaveOperation<T>> ITransactionalEventStore<T>.SaveInTransactionAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		DbConnection connection,
		DbTransaction transaction,
		CancellationToken cancellationToken
	) =>
		SaveCoreAsync(
			aggregate,
			operationContext,
			GetSqlConnection(connection),
			GetSqlTransaction(transaction),
			cancellationToken
		);

	async Task<TransactionalSaveOperation<T>> SaveCoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		SqlConnection? connection,
		SqlTransaction? transaction,
		CancellationToken cancellationToken,
		params EventRecord[] additionalEvents
	)
	{
		operationContext ??= EventStoreOperationContext.DefaultContext();

		FulfilRequirements(aggregate);

		var idempotencyId = operationContext.CorrelationId ?? Activity.Current?.Id ?? $"{Guid.NewGuid()}";
		var validationResult = await GuardAsync(aggregate, cancellationToken);

		if (!validationResult.IsValid)
			return Failed(AggregateSaveFailure.Failure<SaveOutcome<T>>(new AggregateInvalid(validationResult)));

		if (aggregate.Details.Locked)
		{
			return Failed(
				AggregateSaveFailure.Failure<SaveOutcome<T>>(
					new AggregateLocked(aggregate.AggregateType, aggregate.Id())
				)
			);
		}

		if (string.IsNullOrWhiteSpace(aggregate.Details.Id))
			throw new MissingAggregateIdException(idempotencyId);

		_eventStoreTelemetry.SaveCalled(aggregate.Id(), _aggregateTypeFullName, aggregate.AggregateType);
		var activity = _eventStoreTelemetry.SaveAggregate(aggregate.Id(), _aggregateTypeFullName);

		if (!aggregate.HasUnsavedEvents() && (additionalEvents?.Length ?? 0) == 0)
		{
			_eventStoreTelemetry.SaveContainedNoChanges(
				aggregate.Id(),
				_aggregateTypeFullName,
				aggregate.AggregateType
			);
			activity?.Dispose();

			return Outcome(aggregate, SaveOutcomeKind.NoChanges);
		}

		var isNew = aggregate.IsNew();
		var changeEvents =
			(additionalEvents?.Length ?? 0) == 0
				? [.. aggregate.GetUnsavedEvents()]
				: aggregate.GetUnsavedEvents().Concat(additionalEvents!).ToArray();

		if (changeEvents.Length > _eventStoreOptions.Value.MaxEventCountOnSave)
		{
			activity?.Dispose();
			throw new ArgumentOutOfRangeException(
				$"The maximum amount of events to save was exceeded. Attempted: {changeEvents.Length}, Maximum: {_eventStoreOptions.Value.MaxEventCountOnSave}"
			);
		}

		var idempotencyIdAsString = idempotencyId.ToUpperInvariant();
		var idempotencyMarkerId = CreateIdempotencyCheckId(aggregate.Id(), idempotencyIdAsString);

		if (operationContext.UseIdempotencyMarker)
		{
			try
			{
				var existing = connection is null
					? await _client.GetByIdAsync(idempotencyMarkerId, cancellationToken)
					: await _client.GetByIdAsync(idempotencyMarkerId, connection, transaction, cancellationToken);

				if (existing != null)
				{
					_eventStoreTelemetry.EventsAlreadyApplied(aggregate.Id(), idempotencyId);
					activity?.Dispose();
					return Outcome(aggregate, SaveOutcomeKind.AlreadyApplied);
				}
			}
#pragma warning disable CA1031
			catch (Exception ex)
#pragma warning restore CA1031
			{
				_eventStoreTelemetry.GetIdempotencyMarkerFailed(aggregate.Id(), idempotencyId, ex);
			}
		}

		var persisted = await PersistAndNotifyAsync(
			aggregate,
			operationContext,
			idempotencyId,
			changeEvents,
			isNew,
			connection,
			transaction,
			idempotencyIdAsString,
			idempotencyMarkerId,
			activity,
			cancellationToken
		);

		// Only a write actually appended events; a no-op or an idempotent replay appended none.
		if (persisted.Result.IsSuccess && persisted.Result.Value.Kind is SaveOutcomeKind.Written)
			_metrics.EventsAppended(changeEvents.Length, _aggregateTypeShortName);

		return persisted;
	}

	[SuppressMessage(
		"Maintainability",
		"CA1502:Avoid excessive complexity",
		Justification = "Save orchestration handles many interleaved states; keep the flow readable."
	)]
	async Task<TransactionalSaveOperation<T>> PersistAndNotifyAsync(
		T aggregate,
		EventStoreOperationContext operationContext,
		string idempotencyId,
		EventRecord[] changeEvents,
		bool isNew,
		SqlConnection? connection,
		SqlTransaction? transaction,
		string idempotencyIdAsString,
		string idempotencyMarkerId,
		Activity? activity,
		CancellationToken cancellationToken
	)
	{
		if (
			operationContext.NotificationMode.HasFlag(NotificationModes.BeforeDelete)
			&& changeEvents.Any(record => record.Event is Deleted)
		)
			await _aggregateChangeNotifier.BeforeDeleteAsync(aggregate, cancellationToken);
		else if (operationContext.NotificationMode.HasFlag(NotificationModes.BeforeSave))
			await _aggregateChangeNotifier.BeforeSaveAsync(aggregate, isNew, cancellationToken);

		var streamEntity = await GetStreamVersionAsync(
			aggregate.Id(),
			!isNew,
			connection,
			transaction,
			cancellationToken
		);

		if (streamEntity?.IsDeleted == true && !changeEvents.Any(record => record.Event is Restored))
		{
			activity?.Dispose();

			return Failed(
				AggregateSaveFailure.Failure<SaveOutcome<T>>(
					new AggregateDeleted(aggregate.AggregateType, aggregate.Id())
				)
			);
		}

		try
		{
			var previousAggregateVersion = aggregate.Details.SavedVersion;
			var shouldSnapshot = ShouldSnapShot(aggregate, changeEvents, operationContext);
			var now = DateTimeOffset.UtcNow;

			var streamVersionId = streamEntity?.Id ?? CreateStreamVersionId(aggregate.Id());
			SqlServerEventStoreClient.RowData streamVersionRow = new()
			{
				Id = streamVersionId,
				EntityType = StreamVersionType,
				AggregateId = aggregate.Id(),
				AggregateType = aggregate.AggregateType,
				Version = aggregate.Details.CurrentVersion,
				IsDeleted = aggregate.Details.IsDeleted,
				Timestamp = now,
			};

			var userId = _principalAccessor.GetPrincipal()?.FindFirst(operationContext.ClaimIdentifier)?.Value;
			if (operationContext.RequiresValidPrincipalIdentifier && string.IsNullOrWhiteSpace(userId))
				throw new NullReferenceException(
					$"Missing ClaimsPrincipal identifier '{operationContext.ClaimIdentifier}'. Unable to save aggregate."
				);

			List<SqlServerEventStoreClient.RowData> insertRows = [];
			for (var i = 0; i < changeEvents.Length; i++)
			{
				var changeEvent = changeEvents[i].Event;
				var metadata = changeEvents[i].Metadata with
				{
					IdempotencyId = idempotencyIdAsString,
					SchemaVersion = changeEvents[i].Metadata.SchemaVersion,
					UserId = userId,
					CorrelationId = changeEvents[i].Metadata.CorrelationId ?? operationContext.CorrelationId,
				};

				var serializedEvent = await ProtectIfRequiredAsync(
					changeEvent,
					SerializeEvent(changeEvent),
					cancellationToken
				);
				insertRows.Add(
					new SqlServerEventStoreClient.RowData
					{
						Id = CreateEventId(aggregate.Id(), metadata.AggregateVersion),
						EntityType = EventType,
						AggregateId = aggregate.Id(),
						AggregateType = aggregate.AggregateType,
						Version = metadata.AggregateVersion,
						Payload = serializedEvent,
						EventType = _eventNameMapper.GetName<T>(changeEvent),
						IdempotencyId = idempotencyMarkerId,
						SchemaVersion = metadata.SchemaVersion,
						CorrelationId = metadata.CorrelationId,
						CausationId = metadata.CausationId,
						UserId = metadata.UserId,
						Timestamp = now,
					}
				);
			}

			if (operationContext.UseIdempotencyMarker)
			{
				insertRows.Add(
					new SqlServerEventStoreClient.RowData
					{
						Id = idempotencyMarkerId,
						EntityType = IdempotencyMarkerType,
						AggregateId = aggregate.Id(),
						AggregateType = aggregate.AggregateType,
						Version = 0,
						Timestamp = now,
					}
				);
			}

			var requireSnapshotWrite =
				shouldSnapshot
				&& operationContext.RequireSnapshotWrite
				&& _eventStoreOptions.Value.RequireSnapshotWrite;
			SqlServerEventStoreClient.RowData? snapshotRow = requireSnapshotWrite
				? BuildSnapshotRow(aggregate, now)
				: null;

			await SubmitBatchOperationsAsync(
				aggregate,
				idempotencyId,
				streamVersionRow,
				insertRows,
				snapshotRow,
				isNew,
				connection,
				transaction,
				cancellationToken
			);

			if (shouldSnapshot && !requireSnapshotWrite)
				await CreateSnapshotBestEffortAsync(aggregate, connection, transaction, cancellationToken);

			return new TransactionalSaveOperation<T>(
				new SaveOutcome<T>(aggregate, SaveOutcomeKind.Written),
				async afterCommitCancellationToken =>
				{
					try
					{
						FinalizeSuccessfulSave(aggregate, shouldSnapshot);

						if (changeEvents.Any(record => record.Event is Deleted))
							_eventStoreTelemetry.AggregateDeleted(
								aggregate.Id(),
								_aggregateTypeFullName,
								aggregate.AggregateType
							);
						else if (changeEvents.Any(record => record.Event is Restored))
							_eventStoreTelemetry.AggregateRestored(
								aggregate.Id(),
								_aggregateTypeFullName,
								aggregate.AggregateType
							);

						_eventStoreTelemetry.SavedAggregate(
							aggregate.Id(),
							_aggregateTypeFullName,
							changeEvents.Length,
							aggregate.AggregateType
						);

						_eventStoreTelemetry.AggregateSaved(aggregate.AggregateType);
						_eventStoreTelemetry.SaveCompleted(activity, changeEvents.Length);

						await UpdateCacheAsync(aggregate, operationContext.CacheOptions, afterCommitCancellationToken);

						if (
							aggregate.Details.IsDeleted
							&& operationContext.NotificationMode.HasFlag(NotificationModes.AfterDelete)
						)
							await _aggregateChangeNotifier.AfterDeleteAsync(aggregate, afterCommitCancellationToken);
						else if (operationContext.NotificationMode.HasFlag(NotificationModes.AfterSave))
							await _aggregateChangeNotifier.AfterSaveAsync(
								aggregate,
								previousAggregateVersion,
								isNew,
								changeEvents,
								afterCommitCancellationToken
							);
					}
					finally
					{
						activity?.Dispose();
					}
				},
				_ =>
				{
					activity?.Dispose();
					return Task.CompletedTask;
				}
			);
		}
		catch (Exception ex)
		{
			activity?.Dispose();
			ClearCacheFireAndForget(aggregate);

			await HandleSaveFailureAsync(aggregate, operationContext, changeEvents, ex);

			if (ex is ConcurrencyException conflict)
				return Failed(AggregateSaveFailure.Failure<SaveOutcome<T>>(ToConcurrencyConflict(aggregate, conflict)));

			throw;
		}
	}

	static ConcurrencyConflict ToConcurrencyConflict(T aggregate, ConcurrencyException exception) =>
		new(aggregate.AggregateType, aggregate.Id(), exception.VersionAttempted, exception.Version);

	static TransactionalSaveOperation<T> Failed(Result<SaveOutcome<T>, AggregateSaveFailure> failure) => new(failure);

	static TransactionalSaveOperation<T> Outcome(T aggregate, SaveOutcomeKind kind) =>
		new(new SaveOutcome<T>(aggregate, kind));

	async Task HandleSaveFailureAsync(
		T aggregate,
		EventStoreOperationContext operationContext,
		EventRecord[] changeEvents,
		Exception exception
	)
	{
		if (operationContext.NotificationMode.HasFlag(NotificationModes.OnFailure))
		{
			var deleteRequested = changeEvents.Any(record => record.Event is Deleted);
			await _aggregateChangeNotifier.FailureAsync(aggregate, deleteRequested, exception);
		}
	}

	async Task<ValidationResult> GuardAsync(T aggregate, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		return _validator == null
			? await DefaultAggregateValidator<T>.Instance.ValidateAsync(aggregate, cancellationToken)
			: await _validator.ValidateAsync(aggregate, cancellationToken);
	}

	bool ShouldSnapShot(T aggregate, EventRecord[] events, EventStoreOperationContext context)
	{
		// A snapshot serializes aggregate state, which holds the same personal data as the protected
		// events in clear - and one snapshot can span many subjects, so no single key can protect it.
		// With protection enabled the store does not snapshot at all: a snapshot surviving key erasure
		// would make crypto-shredding untrue. Replay from events stays correct, only slower.
		if (_payloadProtector is not null)
			return false;

		// Deleted/restored transitions must always be reflected promptly.
		if (aggregate.Details.IsDeleted || events.Any(record => record.Event is Restored))
			return true;

		// Default to a snapshot on every save (matching the historical behavior) while honoring
		// any per-operation strategy override or selector, so operators can reduce snapshot
		// write amplification on high-frequency aggregates.
		return SnapshotStrategyResolver.ShouldSnapshot(aggregate, events.Length, context, DefaultSnapshotStrategy);
	}

	static readonly IntervalSnapshotStrategy<T> DefaultSnapshotStrategy = new();

	async Task SubmitBatchOperationsAsync(
		T aggregate,
		string idempotencyId,
		SqlServerEventStoreClient.RowData streamVersionRow,
		List<SqlServerEventStoreClient.RowData> insertRows,
		SqlServerEventStoreClient.RowData? snapshotRow,
		bool isNew,
		SqlConnection? connection,
		SqlTransaction? transaction,
		CancellationToken cancellationToken
	)
	{
		try
		{
			if (connection is null)
			{
				await _client.UpsertWithBatchAsync(
					streamVersionRow.Id,
					streamVersionRow.EntityType,
					streamVersionRow.AggregateId,
					streamVersionRow.AggregateType,
					streamVersionRow.Version,
					streamVersionRow.IsDeleted,
					streamVersionRow.Payload,
					streamVersionRow.EventType,
					streamVersionRow.IdempotencyId,
					streamVersionRow.Timestamp,
					insertRows,
					cancellationToken,
					knownNew: isNew,
					snapshotRow: snapshotRow,
					snapshotIsNew: isNew
				);
			}
			else
			{
				await _client.UpsertWithBatchAsync(
					streamVersionRow.Id,
					streamVersionRow.EntityType,
					streamVersionRow.AggregateId,
					streamVersionRow.AggregateType,
					streamVersionRow.Version,
					streamVersionRow.IsDeleted,
					streamVersionRow.Payload,
					streamVersionRow.EventType,
					streamVersionRow.IdempotencyId,
					streamVersionRow.Timestamp,
					insertRows,
					connection,
					transaction!,
					cancellationToken,
					knownNew: isNew,
					snapshotRow: snapshotRow,
					snapshotIsNew: isNew
				);
			}
		}
		// Writes reach storage both directly and through Entity Framework, which wraps the provider
		// exception. Unwrap it so a duplicate event-row key is classified identically on both paths.
		catch (Exception outer) when (outer is SqlException || outer.InnerException is SqlException)
		{
			var ex = outer as SqlException ?? (SqlException)outer.InnerException!;

			_eventStoreTelemetry.SaveFailedAtStorage(aggregate.Id(), _aggregateTypeFullName, ex);

			ClearCacheFireAndForget(aggregate);

			if (ex.Number is 2627 or 2601)
			{
				throw new ConcurrencyException(
					aggregate.Id(),
					idempotencyId,
					aggregate.Details.CurrentVersion,
					aggregate.Details.SavedVersion
				);
			}

			throw new CommitException(
				aggregate.Id(),
				idempotencyId,
				aggregate.Details.CurrentVersion,
				aggregate.Details.SavedVersion,
				ex
			);
		}
		catch (Exception ex)
		{
			_eventStoreTelemetry.SaveFailed(aggregate.Id(), _aggregateTypeFullName, ex);

			ClearCacheFireAndForget(aggregate);

			throw;
		}
	}

	/// <summary>
	/// Builds the snapshot row that is folded into the events batch so the snapshot is written
	/// atomically with the events.
	/// </summary>
	SqlServerEventStoreClient.RowData BuildSnapshotRow(T aggregate, DateTimeOffset now)
	{
		var previousSnapshotVersion = aggregate.Details.SnapshotVersion;
		try
		{
			aggregate.Details.SnapshotVersion = aggregate.Details.CurrentVersion;

			return new SqlServerEventStoreClient.RowData
			{
				Id = CreateSnapshotId(aggregate.Id()),
				EntityType = SnapshotType,
				AggregateId = aggregate.Id(),
				AggregateType = aggregate.AggregateType,
				Version = aggregate.Details.CurrentVersion,
				IsDeleted = aggregate.Details.IsDeleted,
				Payload = SerializeSnapshot(aggregate),
				SchemaVersion = _snapshotSchemaVersion,
				Timestamp = now,
			};
		}
		finally
		{
			aggregate.Details.SnapshotVersion = previousSnapshotVersion;
		}
	}

	/// <summary>
	/// Writes the snapshot best-effort after the events have been committed, logging rather than
	/// failing the save when <see cref="EventStoreOperationContext.RequireSnapshotWrite"/> is false.
	/// </summary>
	async Task CreateSnapshotBestEffortAsync(
		T aggregate,
		SqlConnection? connection,
		SqlTransaction? transaction,
		CancellationToken cancellationToken
	)
	{
		try
		{
			await CreateSnapshotAsync(aggregate, connection, transaction, cancellationToken);
		}
#pragma warning disable CA1031
		catch (Exception ex)
#pragma warning restore CA1031
		{
			_eventStoreTelemetry.SnapshotWriteFailure(aggregate.Id(), _aggregateTypeFullName, ex);
		}
	}

	async Task CreateSnapshotAsync(
		T aggregate,
		SqlConnection? connection,
		SqlTransaction? transaction,
		CancellationToken cancellationToken
	)
	{
		var previousSnapshotVersion = aggregate.Details.SnapshotVersion;

		try
		{
			aggregate.Details.SnapshotVersion = aggregate.Details.CurrentVersion;

			var snapshot = SerializeSnapshot(aggregate);
			var snapshotId = CreateSnapshotId(aggregate.Id());

			if (connection is null)
			{
				await _client.UpsertAsync(
					snapshotId,
					SnapshotType,
					aggregate.Id(),
					_aggregateTypeShortName,
					aggregate.Details.CurrentVersion,
					aggregate.Details.IsDeleted,
					snapshot,
					null,
					null,
					DateTimeOffset.UtcNow,
					schemaVersion: _snapshotSchemaVersion,
					cancellationToken: cancellationToken
				);
			}
			else
			{
				await _client.UpsertAsync(
					snapshotId,
					SnapshotType,
					aggregate.Id(),
					_aggregateTypeShortName,
					aggregate.Details.CurrentVersion,
					aggregate.Details.IsDeleted,
					snapshot,
					null,
					null,
					DateTimeOffset.UtcNow,
					connection,
					transaction!,
					schemaVersion: _snapshotSchemaVersion,
					cancellationToken: cancellationToken
				);
			}
		}
		finally
		{
			aggregate.Details.SnapshotVersion = previousSnapshotVersion;
		}
	}

	static void FinalizeSuccessfulSave(T aggregate, bool shouldSnapshot)
	{
		var currentVersion = aggregate.Details.CurrentVersion;
		aggregate.ClearUnsavedEvents();
		aggregate.Details.CurrentVersion = currentVersion;
		aggregate.Details.SavedVersion = currentVersion;
		aggregate.Details.Etag = currentVersion.ToString(CultureInfo.InvariantCulture);

		if (shouldSnapshot)
			aggregate.Details.SnapshotVersion = currentVersion;
	}

	void ClearCacheFireAndForget(T aggregate)
	{
		Task.Run(async () =>
		{
			try
			{
				var cacheKey = CreateCacheKey(aggregate.Id());
				await _distributedCache.RemoveAsync(cacheKey);
			}
#pragma warning disable CA1031
			catch (Exception ex)
#pragma warning restore CA1031
			{
				_eventStoreTelemetry.CacheRemovalFailure(aggregate.Id(), _aggregateTypeFullName, ex);
			}
		});
	}

	static SqlConnection GetSqlConnection(DbConnection connection) =>
		connection as SqlConnection
		?? throw new InvalidOperationException("SQL Server transactions require a SqlConnection.");

	static SqlTransaction GetSqlTransaction(DbTransaction transaction) =>
		transaction as SqlTransaction
		?? throw new InvalidOperationException("SQL Server transactions require a SqlTransaction.");

	/// <summary>
	/// Encrypts a serialized payload when its event names a data subject.
	/// </summary>
	/// <remarks>
	/// Applied after serialization so the stored payload is the envelope, which keeps the persistence
	/// schema unchanged. An event with no data subject, or a store with no protector configured, is
	/// stored as before.
	/// </remarks>
	Task<string> ProtectIfRequiredAsync(object changeEvent, string serialized, CancellationToken cancellationToken) =>
		_payloadProtector is not null && changeEvent is IEventDataSubject dataSubject
			? _payloadProtector.ProtectAsync(serialized, dataSubject.DataSubjectId, cancellationToken)
			: Task.FromResult(serialized);
}
