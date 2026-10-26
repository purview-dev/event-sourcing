using System.ComponentModel;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Azure.Data.Tables;
using Purview.EventSourcing.AzureStorage.Entities;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.AzureStorage.StorageClients.Table;

sealed class AzureTableClient : IAsyncDisposable
{
	public const int MaximumBatchSize = 100;

	readonly AsyncLazy<TableClient> _tableClient;
	readonly AzureStorageEventStoreOptions _configuration;
	readonly string _tableName;

	readonly ICurrentTenant _currentTenant;

	/// <summary>
	/// Narrows an OData filter to the current tenant.
	/// </summary>
	/// <remarks>
	/// Composed here rather than by each caller: a query that forgets the tenant is the failure this
	/// exists to prevent. An unowned row is visible only to the system scope: treating it as visible
	/// to every tenant would turn a missed stamp into silent cross-tenant exposure.
	/// </remarks>
	string? ScopedFilter(string? filter)
	{
		var scope = _currentTenant.Current;
		if (scope.IsSystem)
			return filter;

		var tenantFilter = $"TenantId eq '{scope.TenantId!.Replace("'", "''", StringComparison.Ordinal)}'";

		return string.IsNullOrWhiteSpace(filter) ? tenantFilter : $"({filter}) and {tenantFilter}";
	}

	void Stamp(ITableEntity? entity)
	{
		if (entity is IHasTenant tenanted)
			tenanted.TenantId = _currentTenant.Current.TenantId;
	}

	bool IsVisible(ITableEntity? entity) =>
		_currentTenant.Current.IsSystem
		|| entity is null
		|| entity is not IHasTenant tenanted
		|| string.Equals(tenanted.TenantId, _currentTenant.Current.TenantId, StringComparison.OrdinalIgnoreCase);

	static readonly string[] SelectedColumnsForEntityExistsQuery = [nameof(ITableEntity.PartitionKey)];

	public AzureTableClient(
		AzureStorageEventStoreOptions configuration,
		string? tableOverride = null,
		ICurrentTenant? currentTenant = null
	)
	{
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		_currentTenant = currentTenant ?? new SystemCurrentTenant();

		_tableName = tableOverride ?? _configuration.Table;
		_tableClient = new(InitializeAsync);
	}

	public async Task<T?> OperationAsync<T>(
		TableTransactionActionType action,
		T entity,
		CancellationToken cancellationToken = default
	)
		where T : class, ITableEntity, new()
	{
		ArgumentNullException.ThrowIfNull(entity, nameof(entity));
		Stamp(entity);

		var table = await _tableClient.GetValueAsync(cancellationToken);
		Azure.Response response;
		switch (action)
		{
			case TableTransactionActionType.Add:
				response = await table.AddEntityAsync(entity, cancellationToken);
				break;
			case TableTransactionActionType.Delete:
				response = await table.DeleteEntityAsync(
					entity.PartitionKey,
					entity.RowKey,
					entity.ETag,
					cancellationToken
				);
				break;
			case TableTransactionActionType.UpdateMerge:
			case TableTransactionActionType.UpdateReplace:
				var updateMode =
					action == TableTransactionActionType.UpdateMerge ? TableUpdateMode.Merge : TableUpdateMode.Replace;
				response = await table.UpdateEntityAsync(entity, entity.ETag, updateMode, cancellationToken);
				break;
			case TableTransactionActionType.UpsertMerge:
			case TableTransactionActionType.UpsertReplace:
				var upsertMode =
					action == TableTransactionActionType.UpsertMerge ? TableUpdateMode.Merge : TableUpdateMode.Replace;
				response = await table.UpsertEntityAsync(entity, upsertMode, cancellationToken);
				break;
			default:
				throw new InvalidEnumArgumentException(nameof(action), (int)action, typeof(TableTransactionActionType));
		}

		var status = response.Status;
		return status >= 400 ? throw new TableOperationException(entity, action, response)
			: action == TableTransactionActionType.Delete ? null
			: entity;
	}

	public async Task<BatchOperationResult> SubmitBatchAsync(
		BatchOperation batchOperation,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(batchOperation, nameof(batchOperation));
		if (!batchOperation.Any())
			throw new InvalidOperationException("No entities in batch.");

		// Stamped once for the whole batch rather than per call site, so a new write path cannot omit
		// it; an unstamped row would then be hidden from its own tenant by the read filter.
		foreach (var action in batchOperation.GetActions())
			Stamp(action.Entity);

		var tableClient = await _tableClient.GetValueAsync(cancellationToken);
		var sets = batchOperation.GetActions().Chunk(size: MaximumBatchSize);

		Dictionary<int, Azure.Response[]> responses = [];
		var idx = 0;
		foreach (var actions in sets)
		{
			var response = await tableClient.SubmitTransactionAsync(actions, cancellationToken);
			if (response != null)
				responses.Add(idx, [.. response.Value]);

			idx++;
		}

		return new(responses);
	}

	public async IAsyncEnumerable<T> QueryEnumerableAsync<T>(
		Expression<Func<T, bool>> whereClause,
		int maxPerPage = 50,
		IEnumerable<string>? fields = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
		where T : class, ITableEntity, new()
	{
		var table = await _tableClient.GetValueAsync(cancellationToken);

		var results = table.QueryAsync(whereClause, maxPerPage, fields, cancellationToken);
		string? continuationToken = null;
		await foreach (var result in results.AsPages(continuationToken, maxPerPage))
		{
			foreach (var entity in result.Values)
				yield return entity;

			continuationToken = result.ContinuationToken;
		}
	}

	public async IAsyncEnumerable<T> QueryEnumerableAsync<T>(
		string? filter = null,
		int maxPerPage = 50,
		IEnumerable<string>? fields = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
		where T : class, ITableEntity, new()
	{
		var table = await _tableClient.GetValueAsync(cancellationToken);

		var results = table.QueryAsync<T>(ScopedFilter(filter), maxPerPage, fields, cancellationToken);
		string? continuationToken = null;
		await foreach (var result in results.AsPages(continuationToken, maxPerPage))
		{
			foreach (var entity in result.Values)
				yield return entity;

			continuationToken = result.ContinuationToken;
		}
	}

	public async Task<ContinuationResponse<T>> QueryAsync<T>(
		Expression<Func<T, bool>> whereClause,
		int maxPerPage = 50,
		IEnumerable<string>? fields = null,
		string? continuationToken = null,
		CancellationToken cancellationToken = default
	)
		where T : class, ITableEntity, new()
	{
		var table = await _tableClient.GetValueAsync(cancellationToken);

		var results = table.QueryAsync(whereClause, maxPerPage, fields, cancellationToken);
		await foreach (var result in results.AsPages(continuationToken, maxPerPage))
		{
			return new() { ContinuationToken = result.ContinuationToken, Results = [.. result.Values] };
		}

		return new();
	}

	public async Task<ContinuationResponse<T>> QueryAsync<T>(
		string? filter = null,
		int maxPerPage = 50,
		IEnumerable<string>? fields = null,
		string? continuationToken = null,
		CancellationToken cancellationToken = default
	)
		where T : class, ITableEntity, new()
	{
		var table = await _tableClient.GetValueAsync(cancellationToken);

		var results = table.QueryAsync<T>(ScopedFilter(filter), maxPerPage, fields, cancellationToken);
		await foreach (var result in results.AsPages(continuationToken, maxPerPage))
		{
			return new() { ContinuationToken = result.ContinuationToken, Results = [.. result.Values] };
		}

		return new();
	}

	public Task<T?> GetAsync<T>(string partitionKey, string rowKey, CancellationToken cancellationToken = default)
		where T : class, ITableEntity, new() => GetAsync<T>(partitionKey, rowKey, null, cancellationToken);

	public async Task<T?> GetAsync<T>(
		string partitionKey,
		string rowKey,
		IEnumerable<string>? fields,
		CancellationToken cancellationToken = default
	)
		where T : class, ITableEntity, new()
	{
		ArgumentNullException.ThrowIfNull(partitionKey, nameof(partitionKey));
		ArgumentNullException.ThrowIfNull(rowKey, nameof(rowKey));

		var table = await _tableClient.GetValueAsync(cancellationToken);
		try
		{
			var pagedResults = table.QueryAsync<T>(
				e => e.PartitionKey == partitionKey && e.RowKey == rowKey,
				maxPerPage: 1,
				select: fields,
				cancellationToken
			);
			// The foreach is because it's a paged result set.
			await foreach (var entity in pagedResults)
				// A point read is by key, so the tenant cannot be part of the query: another tenant's
				// row is filtered out here instead.
				return IsVisible(entity) ? entity : null;

			return null;
			//return await table.GetEntityAsync<T>(partitionKey, rowKey, fields, cancellationToken);
		}
		catch (Azure.RequestFailedException ex) when (ex.Status == 404)
		{
			return null;
		}
	}

	public async Task DeleteTableAsync(CancellationToken cancellationToken = default)
	{
		var table = await _tableClient.GetValueAsync(cancellationToken);
		await table.DeleteAsync(cancellationToken);
	}

	public async Task<bool> EntityExistsAsync(
		string partitionKey,
		string rowKey,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(partitionKey, nameof(partitionKey));
		ArgumentNullException.ThrowIfNull(rowKey, nameof(rowKey));

		var entity = await GetAsync<TableEntity>(
			partitionKey,
			rowKey,
			SelectedColumnsForEntityExistsQuery,
			cancellationToken
		);

		return entity != null;
	}

	async Task<TableClient> InitializeAsync(CancellationToken cancellationToken)
	{
		var tableClient = CreateTableServiceClient().GetTableClient(_tableName);
		await tableClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

		return tableClient;
	}

	TableServiceClient CreateTableServiceClient()
	{
		TableClientOptions clientOptions = new();
		if (_configuration.TimeoutInSeconds.HasValue)
			clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(_configuration.TimeoutInSeconds.Value);

		return new(_configuration.ConnectionString, clientOptions);
	}

	public async ValueTask DisposeAsync()
	{
		if (_tableClient.IsValueCreated)
		{
			await _tableClient.DisposeAsync();
		}
	}
}
