using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Purview.EventSourcing.CosmosDb.Snapshots;
using Purview.EventSourcing.EntityFrameworkCore.Query;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.CosmosDb.Client;

/// <summary>
/// Reads and writes aggregate snapshots through Entity Framework, scoping every operation to the
/// aggregate type's partition.
/// </summary>
/// <typeparam name="T">The aggregate type.</typeparam>
sealed class CosmosDbSnapshotClient<T> : IAsyncDisposable
	where T : class
{
	// Entity Framework builds - and caches - an internal service provider per distinct set of context
	// options, so one set is shared by every store that talks to the same account.
	static readonly ConcurrentDictionary<
		ContextOptionsKey,
		DbContextOptions<SnapshotDbContext<T>>
	> ContextOptionsCache = new();

	readonly CosmosDbEventStoreOptions _options;
	readonly CosmosDbContainerProvisioner _provisioner;
	readonly DbContextOptions<SnapshotDbContext<T>> _contextOptions;
	readonly string _containerName;
	readonly ICurrentTenant _currentTenant;

	/// <summary>
	/// Narrows a snapshot query to the current tenant.
	/// </summary>
	/// <remarks>
	/// Applied after <c>WithPartitionKey</c> rather than as a global query filter: EF Cosmos requires the
	/// partition-key call to sit on a plain entity query root, and a global filter stops it being one.
	/// A document with no recorded owner stays visible.
	/// </remarks>
	IQueryable<T> Scoped(IQueryable<T> query)
	{
		var scope = _currentTenant.Current;

		return scope.IsSystem
			? query
			: query.Where(candidate =>
				EF.Property<string>(candidate, SnapshotDbContext<T>.TenantPropertyName) == scope.TenantId
				|| EF.Property<string>(candidate, SnapshotDbContext<T>.TenantPropertyName) == null
			);
	}

	readonly string _aggregateTypeName;

	readonly record struct ContextOptionsKey(
		string ConnectionString,
		string Database,
		Microsoft.Azure.Cosmos.ConnectionMode ConnectionMode,
		int? RequestTimeoutInSeconds,
		bool IgnoreSSLWarnings,
		CosmosDbClientOverrides? Overrides
	);

	public CosmosDbSnapshotClient(
		CosmosDbEventStoreOptions options,
		string aggregateTypeName,
		CosmosDbClientOverrides? overrides = null,
		string? containerNameOverride = null,
		ICurrentTenant? currentTenant = null
	)
	{
		ArgumentNullException.ThrowIfNull(options);
		_currentTenant = currentTenant ?? new SystemCurrentTenant();

		// Snapshot documents are partitioned by aggregate type, and the mapping depends on it: a
		// container built around any other path would not match the model.
		if (!string.Equals(options.PartitionKeyPath, SnapshotDbContext<T>.PartitionKeyPath, StringComparison.Ordinal))
		{
			throw new InvalidOperationException(
				$"Snapshot containers must use the partition key path '{SnapshotDbContext<T>.PartitionKeyPath}', but '{options.PartitionKeyPath}' was configured."
			);
		}

		_options = options;
		_aggregateTypeName = aggregateTypeName;
		_containerName = containerNameOverride ?? options.Container;
		_provisioner = new(options, containerNameOverride: _containerName, overrides: overrides);
		_contextOptions = ContextOptionsCache.GetOrAdd(
			new ContextOptionsKey(
				options.ConnectionString,
				options.Database,
				options.ConnectionMode,
				options.RequestTimeoutInSeconds,
				options.IgnoreSSLWarnings,
				overrides
			),
			_ => SnapshotDbContext<T>.BuildContextOptions(options, overrides)
		);
	}

	public Task EnsureCreatedAsync(CancellationToken cancellationToken = default) =>
		_provisioner.EnsureCreatedAsync(cancellationToken);

	public Task DeleteContainerAsync(CancellationToken cancellationToken = default) =>
		_provisioner.DeleteContainerAsync(cancellationToken);

	/// <summary>
	/// Writes the snapshot, creating the document when it does not already exist.
	/// </summary>
	/// <remarks>
	/// Entity Framework has no upsert for Cosmos DB - an added entity issues <c>CreateItem</c> and a
	/// modified one <c>ReplaceItem</c> - so existence is established first, which costs an additional
	/// request per snapshot.
	/// </remarks>
	public async Task<bool> UpsertAsync(T aggregate, string id, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(aggregate);

		await EnsureCreatedAsync(cancellationToken);
		await using var context = CreateContext();

		var exists = await Scoped(context.Snapshots.WithPartitionKey(_aggregateTypeName).AsNoTracking())
			.AnyAsync(
				candidate => EF.Property<string>(candidate, SnapshotDbContext<T>.IdPropertyName) == id,
				cancellationToken
			);

		// Entry() rather than Attach(): the shadow key is still null here, and Attach would start
		// tracking - and reject it. A detached entry takes the key first, then begins tracking.
		var entry = context.Entry(aggregate);
		entry.Property(SnapshotDbContext<T>.IdPropertyName).CurrentValue = id;
		entry.Property(SnapshotDbContext<T>.PartitionKeyPropertyName).CurrentValue = _aggregateTypeName;
		entry.Property(SnapshotDbContext<T>.TenantPropertyName).CurrentValue = _currentTenant.Current.TenantId;
		entry.State = exists ? EntityState.Modified : EntityState.Added;

		return await context.SaveChangesAsync(cancellationToken) > 0;
	}

	public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);

		await EnsureCreatedAsync(cancellationToken);
		await using var context = CreateContext();

		var existing = await Scoped(context.Snapshots.WithPartitionKey(_aggregateTypeName))
			.SingleOrDefaultAsync(
				candidate => EF.Property<string>(candidate, SnapshotDbContext<T>.IdPropertyName) == id,
				cancellationToken
			);

		if (existing is null)
			return false;

		context.Remove(existing);
		return await context.SaveChangesAsync(cancellationToken) > 0;
	}

	public async Task<long> CountAsync(
		Expression<Func<T, bool>>? whereClause,
		CancellationToken cancellationToken = default
	)
	{
		await EnsureCreatedAsync(cancellationToken);
		await using var context = CreateContext();

		return await BuildQuery(context, whereClause, orderByClause: null, applyDefaultOrdering: false)
			.LongCountAsync(cancellationToken);
	}

	/// <summary>
	/// Reads a single page, resuming from <paramref name="continuationToken"/> when one is supplied.
	/// </summary>
	/// <returns>The page's values and the token for the next page, which is null once the last page has been read.</returns>
	public async Task<(IReadOnlyList<T> Values, string? ContinuationToken)> QueryPageAsync(
		Expression<Func<T, bool>>? whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		int maxRecords,
		string? continuationToken,
		CancellationToken cancellationToken = default
	)
	{
		await EnsureCreatedAsync(cancellationToken);
		await using var context = CreateContext();

		var query = BuildQuery(context, whereClause, orderByClause, applyDefaultOrdering: true);

		if (TryReadOffset(continuationToken, out var offset))
			return await ReadOffsetPageAsync(query, offset, maxRecords, cancellationToken);

		// ToPageAsync is the only API that exposes Cosmos DB continuation tokens; paging with Skip/Take
		// instead would re-read every preceding page. It is experimental in EF, hence the suppression.
#pragma warning disable EF9102
		var page = await query.ToPageAsync(maxRecords, continuationToken, cancellationToken: cancellationToken);
#pragma warning restore EF9102

		if (page.Values.Count <= maxRecords)
			return (page.Values, page.ContinuationToken);

		// The page size is a request hint, and a backend that ignores it returns a token positioned past
		// more rows than the caller asked for - so the token cannot be handed back. OFFSET/LIMIT is part
		// of the query itself and is always applied, so paging continues through an offset token instead.
		return BuildOffsetPage(page.Values, offset: 0, maxRecords);
	}

	const string OffsetTokenPrefix = "o:";

	static bool TryReadOffset(string? continuationToken, out int offset)
	{
		offset = 0;

		return continuationToken?.StartsWith(OffsetTokenPrefix, StringComparison.Ordinal) == true
			&& int.TryParse(
				continuationToken.AsSpan(OffsetTokenPrefix.Length),
				System.Globalization.CultureInfo.InvariantCulture,
				out offset
			);
	}

	static async Task<(IReadOnlyList<T> Values, string? ContinuationToken)> ReadOffsetPageAsync(
		IQueryable<T> query,
		int offset,
		int maxRecords,
		CancellationToken cancellationToken
	)
	{
		// One row beyond the page shows whether a further page exists.
		var values = await query.Skip(offset).Take(maxRecords + 1).ToListAsync(cancellationToken);

		return BuildOffsetPage(values, offset, maxRecords);
	}

	static (IReadOnlyList<T> Values, string? ContinuationToken) BuildOffsetPage(
		IReadOnlyList<T> values,
		int offset,
		int maxRecords
	) => ([.. values.Take(maxRecords)], values.Count > maxRecords ? $"{OffsetTokenPrefix}{offset + maxRecords}" : null);

	IQueryable<T> BuildQuery(
		SnapshotDbContext<T> context,
		Expression<Func<T, bool>>? whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		bool applyDefaultOrdering
	)
	{
		var query = Scoped(context.Snapshots.WithPartitionKey(_aggregateTypeName).AsNoTracking());

		if (whereClause is not null)
			query = query.Where(SnapshotPredicateRewriter.Rewrite(whereClause, _aggregateTypeName));

		if (orderByClause is not null)
		{
			query = orderByClause(query);
			SnapshotPredicateRewriter.EnsureSupportedOrderByExpression(query.Expression);
		}
		else if (applyDefaultOrdering)
		{
			// Paging without an order is not stable across continuations.
			query = query.OrderBy(candidate => EF.Property<string>(candidate, SnapshotDbContext<T>.IdPropertyName));
		}

		return query;
	}

	SnapshotDbContext<T> CreateContext() => new(_contextOptions, _options.Database, _containerName);

	public async ValueTask DisposeAsync() => await _provisioner.DisposeAsync();
}
