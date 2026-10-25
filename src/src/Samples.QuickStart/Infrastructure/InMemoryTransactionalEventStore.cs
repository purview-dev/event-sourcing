using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.Internal;
using Purview.Results;

namespace Purview.EventSourcing.Samples.QuickStart.Infrastructure;

[SuppressMessage("Naming", "PDS0004:Use correct acronym capitalization", Justification = "Matches source.")]
sealed class InMemoryTransactionalEventStore<T>(InMemoryFailurePlan failurePlan)
	: IQueryableEventStoreCore<T>,
		ITransactionalEventStore<T>
	where T : class, IAggregate, new()
{
	static readonly ConcurrentDictionary<string, string> Persisted = new(StringComparer.OrdinalIgnoreCase);

	static Result<T, AggregateReadFailure> NotFound(string aggregateId) =>
		AggregateReadFailure.Failure<T>(new AggregateNotFound(typeof(T).Name, aggregateId));

	public string TransactionBoundaryKey => "quickstart-inmemory";

	public Task<T> CreateAsync(string? aggregateId = null, CancellationToken cancellationToken = default)
	{
		T aggregate = new();
#pragma warning disable CA1308 // Normalize strings to uppercase
		aggregate.Details.Id = string.IsNullOrWhiteSpace(aggregateId)
			? $"{typeof(T).Name.ToLowerInvariant()}-{Guid.NewGuid():N}"
			: aggregateId;
#pragma warning restore CA1308 // Normalize strings to uppercase

		return Task.FromResult(aggregate);
	}

	public async Task<Result<T, AggregateReadFailure>> GetOrCreateAsync(
		string? aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		if (!string.IsNullOrWhiteSpace(aggregateId))
		{
			var existing = await GetAsync(aggregateId, operationContext, cancellationToken);
			if (existing.IsSuccess)
				return existing;
		}

		return await CreateAsync(aggregateId, cancellationToken);
	}

	public Task<Result<T, AggregateReadFailure>> GetAsync(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) =>
		Task.FromResult<Result<T, AggregateReadFailure>>(
			Persisted.TryGetValue(aggregateId, out var json) ? Deserialize(json) : NotFound(aggregateId)
		);

	public async Task<Result<T, AggregateReadFailure>> GetAtAsync(
		string aggregateId,
		long version,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		var result = await GetAsync(aggregateId, operationContext, cancellationToken);

		return result.IsSuccess && result.Value.Details.CurrentVersion == version ? result : NotFound(aggregateId);
	}

	public Task<Result<SaveOutcome<T>, AggregateSaveFailure>> SaveAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		if (!aggregate.HasUnsavedEvents())
			return Task.FromResult(CreateSkippedResult(aggregate));

		ThrowIfConfiguredToFail(aggregate);

		Persisted[aggregate.Id()] = Serialize(aggregate);
		MarkAsSaved(aggregate);

		return Task.FromResult(CreateSavedResult(aggregate));
	}

	public Task<Result<T, AggregateReadFailure>> GetDeletedAsync(
		string aggregateId,
		CancellationToken cancellationToken = default
	) => Task.FromResult(NotFound(aggregateId));

	public Task<Result<SaveOutcome<T>, AggregateDeleteFailure>> DeleteAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => throw new NotSupportedException("The quick start store only demonstrates create, load, query, and save.");

	public Task<Result<SaveOutcome<T>, AggregateRestoreFailure>> RestoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => throw new NotSupportedException("The quick start store only demonstrates create, load, query, and save.");

	public async IAsyncEnumerable<string> GetAggregateIdsAsync(
		bool includeDeleted,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
	{
		foreach (var aggregateId in Persisted.Keys.Order(StringComparer.OrdinalIgnoreCase))
			yield return aggregateId;

		await Task.CompletedTask;
	}

	public Task<AggregateExistence> ExistsAsync(string aggregateId, CancellationToken cancellationToken = default)
	{
		return !Persisted.TryGetValue(aggregateId, out var json)
			? Task.FromResult(AggregateExistence.DoesNotExist)
			: Task.FromResult(new AggregateExistence(ExistenceStatus.Exists, Deserialize(json).Details.CurrentVersion));
	}

	public T FulfilRequirements(T aggregate) => aggregate;

	public IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync(
		string aggregateId,
		long versionFrom,
		long? versionTo,
		CancellationToken cancellationToken
	) => throw new NotSupportedException("The quick start store only demonstrates create, load, query, and save.");

	public async IAsyncEnumerable<T> GetQueryEnumerableAsync(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		int maxRecordsPerIteration = ContinuationRequest.DefaultMaxRecords,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
	{
		foreach (var aggregate in ApplyOrdering(ReadCommitted().AsQueryable().Where(whereClause), orderByClause))
			yield return aggregate;

		await Task.CompletedTask;
	}

	public async IAsyncEnumerable<T> GetListEnumerableAsync(
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		int maxRecordsPerIteration = ContinuationRequest.DefaultMaxRecords,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
	{
		foreach (var aggregate in ApplyOrdering(ReadCommitted().AsQueryable(), orderByClause))
			yield return aggregate;

		await Task.CompletedTask;
	}

	public Task<ContinuationResponse<T>> QueryAsync(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		ContinuationRequest request,
		CancellationToken cancellationToken = default
	) =>
		Task.FromResult(
			CreateContinuationResponse(
				ApplyOrdering(ReadCommitted().AsQueryable().Where(whereClause), orderByClause),
				request
			)
		);

	public Task<ContinuationResponse<T>> ListAsync(
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		ContinuationRequest request,
		CancellationToken cancellationToken = default
	) =>
		Task.FromResult(
			CreateContinuationResponse(ApplyOrdering(ReadCommitted().AsQueryable(), orderByClause), request)
		);

	public Task<long> CountAsync(Expression<Func<T, bool>>? whereClause, CancellationToken cancellationToken = default)
	{
		var query = ReadCommitted().AsQueryable();
		return Task.FromResult(whereClause is null ? query.LongCount() : query.LongCount(whereClause));
	}

	public Task<T?> SingleOrDefaultAsync(
		Expression<Func<T, bool>> whereClause,
		CancellationToken cancellationToken = default
	) => Task.FromResult(ReadCommitted().AsQueryable().SingleOrDefault(whereClause));

	public Task<T?> FirstOrDefaultAsync(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		CancellationToken cancellationToken = default
	) =>
		Task.FromResult(
			ApplyOrdering(ReadCommitted().AsQueryable().Where(whereClause), orderByClause).FirstOrDefault()
		);

	public DbConnection CreateTransactionConnection() => new FakeDbConnection();

	public Task EnsureTransactionConfiguredAsync(
		DbConnection connection,
		CancellationToken cancellationToken = default
	) => Task.CompletedTask;

	public Task<TransactionalSaveOperation<T>> SaveInTransactionAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		DbConnection connection,
		DbTransaction transaction,
		CancellationToken cancellationToken = default
	)
	{
		if (!aggregate.HasUnsavedEvents())
			return Task.FromResult(new TransactionalSaveOperation<T>(CreateSkippedResult(aggregate)));

		ThrowIfConfiguredToFail(aggregate);

		var committedJson = Serialize(aggregate);
		return Task.FromResult(
			new TransactionalSaveOperation<T>(
				CreateSavedResult(aggregate),
				_ =>
				{
					Persisted[aggregate.Id()] = committedJson;
					MarkAsSaved(aggregate);
					return Task.CompletedTask;
				}
			)
		);
	}

	static IQueryable<T> ApplyOrdering(IQueryable<T> query, Func<IQueryable<T>, IQueryable<T>>? orderByClause) =>
		orderByClause?.Invoke(query) ?? query.OrderBy(aggregate => aggregate.Details.Id);

	static ContinuationResponse<T> CreateContinuationResponse(IQueryable<T> query, ContinuationRequest request)
	{
		var skip = string.IsNullOrWhiteSpace(request.ContinuationToken)
			? 0
			: int.Parse(request.ContinuationToken, CultureInfo.InvariantCulture);
		var results = query.Skip(skip).Take(request.MaxRecords).ToArray();
		var next = skip + results.Length;
		var total = query.Count();

		return new ContinuationResponse<T>
		{
			RequestedCount = request.MaxRecords,
			Results = results,
			ContinuationToken = next < total ? next.ToString(CultureInfo.InvariantCulture) : null,
		};
	}

	static IEnumerable<T> ReadCommitted() => Persisted.Values.Select(Deserialize);

	static string Serialize(T aggregate) => JsonSerializer.Serialize(aggregate);

	static T Deserialize(string json)
	{
		var aggregate =
			JsonSerializer.Deserialize<T>(json)
			?? throw new InvalidOperationException($"Unable to deserialize aggregate {typeof(T).Name}.");
		aggregate.ClearUnsavedEvents();
		aggregate.Details.SavedVersion = aggregate.Details.CurrentVersion;
		return aggregate;
	}

	static Result<SaveOutcome<T>, AggregateSaveFailure> CreateSavedResult(T aggregate) =>
		new SaveOutcome<T>(aggregate, SaveOutcomeKind.Written);

	static Result<SaveOutcome<T>, AggregateSaveFailure> CreateSkippedResult(T aggregate) =>
		new SaveOutcome<T>(aggregate, SaveOutcomeKind.NoChanges);

	static void MarkAsSaved(T aggregate)
	{
		aggregate.ClearUnsavedEvents();
		aggregate.Details.SavedVersion = aggregate.Details.CurrentVersion;
	}

	void ThrowIfConfiguredToFail(T aggregate)
	{
		if (failurePlan.ShouldFail(typeof(T), aggregate.Id()))
		{
			throw new InvalidOperationException($"Simulated failure while saving {typeof(T).Name} '{aggregate.Id()}'.");
		}
	}

	sealed class FakeDbConnection : DbConnection
	{
		ConnectionState _state = ConnectionState.Closed;

		[AllowNull]
		public override string ConnectionString { get; set; } = "quickstart";

		public override string Database => "quickstart";

		public override string DataSource => "quickstart";

		public override string ServerVersion => "1.0";

		public override ConnectionState State => _state;

		public override void ChangeDatabase(string databaseName) { }

		public override void Close() => _state = ConnectionState.Closed;

		public override void Open() => _state = ConnectionState.Open;

		public override Task OpenAsync(CancellationToken cancellationToken)
		{
			Open();
			return Task.CompletedTask;
		}

		protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
			new FakeDbTransaction(this);

		protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
	}

	sealed class FakeDbTransaction(FakeDbConnection connection) : DbTransaction
	{
		public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

		protected override DbConnection DbConnection => connection;

		public override void Commit() { }

		public override void Rollback() { }

		public override Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public override Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
