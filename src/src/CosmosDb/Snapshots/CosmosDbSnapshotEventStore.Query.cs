using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Purview.EventSourcing.CosmosDb.Snapshot;

partial class CosmosDbSnapshotEventStore<T>
{
	/// <inheritdoc/>
	public async IAsyncEnumerable<T> GetQueryEnumerableAsync(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		int maxRecordsPerIteration = ContinuationRequest.DefaultMaxRecords,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(whereClause, nameof(whereClause));

		await foreach (
			var aggregate in EnumerateAsync(whereClause, orderByClause, maxRecordsPerIteration, cancellationToken)
		)
			yield return aggregate;
	}

	/// <inheritdoc/>
	public async IAsyncEnumerable<T> GetListEnumerableAsync(
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		int maxRecordsPerIteration = ContinuationRequest.DefaultMaxRecords,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
	{
		await foreach (var aggregate in EnumerateAsync(null, orderByClause, maxRecordsPerIteration, cancellationToken))
			yield return aggregate;
	}

	/// <inheritdoc/>
	public async Task<ContinuationResponse<T>> QueryAsync(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		ContinuationRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(whereClause, nameof(whereClause));
		ArgumentNullException.ThrowIfNull(request, nameof(request));

		using var activity = _telemetry.QuerySnapshots("Query", typeof(T).FullName ?? typeof(T).Name);

		return await ExecuteQueryAsync(whereClause, orderByClause, request, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ContinuationResponse<T>> ListAsync(
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		ContinuationRequest request,
		CancellationToken cancellationToken = default
	)
	{
		using var activity = _telemetry.QuerySnapshots("List", typeof(T).FullName ?? typeof(T).Name);

		ArgumentNullException.ThrowIfNull(request, nameof(request));

		return await ExecuteQueryAsync(null, orderByClause, request, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<T?> SingleOrDefaultAsync(
		Expression<Func<T, bool>> whereClause,
		CancellationToken cancellationToken = default
	)
	{
		using var activity = _telemetry.QuerySnapshots("SingleOrDefault", typeof(T).FullName ?? typeof(T).Name);

		// Leave as 2 as it'll throw when expected.
		var query = await GetSpecificNumberAsync(whereClause, null, 2, cancellationToken: cancellationToken);
		return query.SingleOrDefault();
	}

	/// <inheritdoc/>
	public async Task<T?> FirstOrDefaultAsync(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		CancellationToken cancellationToken = default
	)
	{
		using var activity = _telemetry.QuerySnapshots("FirstOrDefault", typeof(T).FullName ?? typeof(T).Name);

		var query = await GetSpecificNumberAsync(whereClause, orderByClause, 1, cancellationToken: cancellationToken);
		return query.FirstOrDefault();
	}

	/// <inheritdoc/>
	public Task<long> CountAsync(
		Expression<Func<T, bool>>? whereClause,
		CancellationToken cancellationToken = default
	) => _cosmosDbClient.CountAsync(whereClause, cancellationToken);

	async Task<IEnumerable<T>> GetSpecificNumberAsync(
		Expression<Func<T, bool>> whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		int maxCount,
		CancellationToken cancellationToken = default
	)
	{
		var query = await QueryAsync(
			whereClause,
			orderByClause,
			request: new ContinuationRequest { MaxRecords = maxCount },
			cancellationToken: cancellationToken
		);

		return query.Results;
	}

	async IAsyncEnumerable<T> EnumerateAsync(
		Expression<Func<T, bool>>? whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		int maxRecordsPerIteration,
		[EnumeratorCancellation] CancellationToken cancellationToken
	)
	{
		ContinuationRequest request = new() { MaxRecords = maxRecordsPerIteration };
		ContinuationResponse<T> response;
		do
		{
			response = await ExecuteQueryAsync(whereClause, orderByClause, request, cancellationToken);
			foreach (var result in response.Results)
				yield return result;

			request = response;
		} while (response.HasMoreRecords);
	}

	async Task<ContinuationResponse<T>> ExecuteQueryAsync(
		Expression<Func<T, bool>>? whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		ContinuationRequest request,
		CancellationToken cancellationToken
	)
	{
		var (values, continuationToken) = await _cosmosDbClient.QueryPageAsync(
			whereClause,
			orderByClause,
			request.MaxRecords,
			request.ContinuationToken,
			cancellationToken
		);

		var results = values.Select(FulfilRequirements).ToArray();

		return new ContinuationResponse<T>
		{
			Results = results,
			RequestedCount = request.MaxRecords,
			ContinuationToken = continuationToken,
			TotalCount = request.IncludeTotalCount
				? await _cosmosDbClient.CountAsync(whereClause, cancellationToken)
				: null,
		};
	}
}
