using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Purview.EventSourcing.EntityFrameworkCore.Mapping;
using Purview.EventSourcing.EntityFrameworkCore.Query;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.SqlServer.Client;

sealed partial class SqlServerClient
{
	static readonly ConcurrentDictionary<string, SemaphoreSlim> EnsureTableLocks = new(StringComparer.Ordinal);
	static readonly ConcurrentDictionary<string, byte> EnsuredTables = new(StringComparer.Ordinal);
	static readonly HashSet<string> SupportedSnapshotIncludeColumns = ["Id", "AggregateType", "Payload"];

	readonly SqlServerClientOptions _options;
	readonly ICurrentTenant _currentTenant;
	readonly string _tableEnsureKey;

	public SqlServerClient(SqlServerClientOptions options, ICurrentTenant? currentTenant = null)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_currentTenant = currentTenant ?? new SystemCurrentTenant();
		_tableEnsureKey = $"{_options.ConnectionString}|{_options.SchemaName}|{_options.TableName}";

		ValidateIdentifier(_options.SchemaName);
		ValidateIdentifier(_options.TableName);
		SqlServerJsonIndexSchemaManager.ValidateOrThrow(
			_options.JsonIndexOptions,
			_options.SchemaName,
			_options.TableName,
			SupportedSnapshotIncludeColumns,
			nameof(SqlServerClientOptions.JsonIndexOptions)
		);
	}

	public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
	{
		if (!_options.AutoCreateTable)
			return;

		await EnsureTableIfEnabledAsync(cancellationToken);
	}

	public async Task EnsureTableExistsAsync(SqlConnection connection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(connection);

		if (!_options.AutoCreateTable)
			return;

		await EnsureTableIfEnabledAsync(connection, transaction: null, cancellationToken);
	}

	public async Task<bool> UpsertAsync<T>(
		T aggregate,
		string id,
		string aggregateType,
		CancellationToken cancellationToken = default
	)
		where T : class
	{
		await EnsureTableIfEnabledAsync(cancellationToken);

		await using var queryContext = CreateQueryContext<T>();
		return await UpsertAsync(aggregate, id, aggregateType, queryContext, cancellationToken);
	}

	public async Task<bool> UpsertAsync<T>(
		T aggregate,
		string id,
		string aggregateType,
		SqlConnection connection,
		SqlTransaction transaction,
		CancellationToken cancellationToken = default
	)
		where T : class
	{
		ArgumentNullException.ThrowIfNull(connection);
		ArgumentNullException.ThrowIfNull(transaction);

		await EnsureTableIfEnabledAsync(connection, transaction, cancellationToken);

		await using var queryContext = CreateQueryContext<T>(connection);
		await queryContext.Database.UseTransactionAsync(transaction, cancellationToken);
		return await UpsertAsync(aggregate, id, aggregateType, queryContext, cancellationToken);
	}

	public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
	{
		await EnsureTableIfEnabledAsync(cancellationToken);
		await using var context = CreateStorageContext();
		var existing = await context.Snapshots.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
		if (existing is null)
			return false;

		context.Snapshots.Remove(existing);
		return await context.SaveChangesAsync(cancellationToken) > 0;
	}

	public async Task<long> CountByAggregateTypeAsync(
		string aggregateType,
		CancellationToken cancellationToken = default
	)
	{
		await EnsureTableIfEnabledAsync(cancellationToken);
		await using var context = CreateStorageContext();
		return await context
			.Snapshots.AsNoTracking()
			.LongCountAsync(s => s.AggregateType == aggregateType, cancellationToken);
	}

	public async Task<long> CountByAggregateTypeAsync<T>(
		string aggregateType,
		Expression<Func<T, bool>>? whereClause,
		CancellationToken cancellationToken = default
	)
		where T : class
	{
		await using var context = CreateQueryContext<T>();
		await EnsureTableIfEnabledAsync(cancellationToken);
		var query = BuildAggregateQuery(context, aggregateType, whereClause);
		return await query.LongCountAsync(cancellationToken);
	}

	public async Task<List<T>> QueryByAggregateTypeAsync<T>(
		string aggregateType,
		Expression<Func<T, bool>>? whereClause,
		Func<IQueryable<T>, IQueryable<T>>? orderByClause,
		int skipCount,
		int takeCount,
		CancellationToken cancellationToken = default
	)
		where T : class
	{
		await using var context = CreateQueryContext<T>();
		await EnsureTableIfEnabledAsync(cancellationToken);
		var normalizedWhereClause = whereClause is null
			? null
			: SnapshotPredicateRewriter.Rewrite(whereClause, aggregateType);

		var rowQuery = context.Snapshots.AsNoTracking().AsSplitQuery().Where(s => s.AggregateType == aggregateType);

		var aggregateQuery = rowQuery.OrderBy(s => s.Id).Select(s => s.Payload);
		if (normalizedWhereClause != null)
			aggregateQuery = aggregateQuery.Where(normalizedWhereClause);

		if (orderByClause != null)
		{
			aggregateQuery = orderByClause(aggregateQuery);
			SnapshotPredicateRewriter.EnsureSupportedOrderByExpression(aggregateQuery.Expression);
		}

		var page = aggregateQuery.Skip(Math.Max(0, skipCount)).Take(Math.Max(0, takeCount));
		return await page.ToListAsync(cancellationToken);
	}

	public async Task<T?> GetByIdAsync<T>(string id, CancellationToken cancellationToken = default)
		where T : class
	{
		await using var context = CreateQueryContext<T>();
		await EnsureTableIfEnabledAsync(cancellationToken);
		var query = context.Snapshots.AsNoTracking().AsSplitQuery().Where(s => s.Id == id).Select(s => s.Payload);
		return await query.SingleOrDefaultAsync(cancellationToken);
	}

	static IQueryable<T> BuildAggregateQuery<T>(
		SnapshotQueryDbContext<T> context,
		string aggregateType,
		Expression<Func<T, bool>>? whereClause
	)
		where T : class
	{
		var query = context
			.Snapshots.AsNoTracking()
			.Where(s => s.AggregateType == aggregateType)
			.Select(s => s.Payload);

		return whereClause is null ? query : query.Where(SnapshotPredicateRewriter.Rewrite(whereClause, aggregateType));
	}

	static async Task<bool> UpsertAsync<T>(
		T aggregate,
		string id,
		string aggregateType,
		SnapshotQueryDbContext<T> context,
		CancellationToken cancellationToken
	)
		where T : class
	{
		ArgumentNullException.ThrowIfNull(aggregate);
		var exists = await context.Snapshots.AsNoTracking().AnyAsync(s => s.Id == id, cancellationToken);
		SnapshotQueryRow<T> entity = new()
		{
			Id = id,
			AggregateType = aggregateType,
			Payload = aggregate,
			// Stamped from the context that will write it, so a snapshot cannot be saved without an
			// owner and then be hidden from its own tenant by the query filter.
			TenantId = context.TenantId,
		};

		if (exists)
			context.Update(entity);
		else
			context.Add(entity);

		return await context.SaveChangesAsync(cancellationToken) > 0;
	}

	SnapshotStorageDbContext CreateStorageContext() => new(_options, _currentTenant);

	SnapshotStorageDbContext CreateStorageContext(SqlConnection connection) =>
		new(_options, connection, _currentTenant);

	SnapshotQueryDbContext<T> CreateQueryContext<T>()
		where T : class => new(_options);

	SnapshotQueryDbContext<T> CreateQueryContext<T>(SqlConnection connection)
		where T : class => new(_options, connection);

	static void ValidateIdentifier(string identifier)
	{
		if (string.IsNullOrWhiteSpace(identifier))
			throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));

		if (!IdentifierRegex().IsMatch(identifier))
			throw new ArgumentException($"Identifier '{identifier}' contains invalid characters.", nameof(identifier));
	}

	[GeneratedRegex(@"^[\w\-\.]+$")]
	private static partial Regex IdentifierRegex();

	sealed class SnapshotStorageDbContext : DbContext, ISnapshotModelCacheKeySource
	{
		readonly SqlServerClientOptions _options;
		readonly SqlConnection? _connection;

		// Captured once per context: a context is scoped to one operation, and EF only re-evaluates
		// plain context fields when it reuses the cached model.
		bool _crossesTenants = true;
		string? _tenantId;

		/// <summary>The tenant owning rows written through this context.</summary>
		public string? TenantId => _tenantId;

		void SetTenant(ICurrentTenant? currentTenant)
		{
			var scope = (currentTenant ?? new SystemCurrentTenant()).Current;
			_crossesTenants = scope.IsSystem;
			_tenantId = scope.TenantId;
		}

		public SnapshotStorageDbContext(SqlServerClientOptions options, ICurrentTenant? currentTenant = null)
		{
			_options = options;
			SetTenant(currentTenant);
		}

		public SnapshotStorageDbContext(
			SqlServerClientOptions options,
			SqlConnection connection,
			ICurrentTenant? currentTenant = null
		)
		{
			_options = options;
			_connection = connection;
			SetTenant(currentTenant);
		}

		public DbSet<SnapshotStorageRow> Snapshots => Set<SnapshotStorageRow>();

		public object CreateModelCacheKey(bool designTime) =>
			(typeof(SnapshotStorageDbContext), _options.SchemaName, _options.TableName, designTime);

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			if (_connection is null)
				optionsBuilder.UseSqlServer(_options.ConnectionString);
			else
				optionsBuilder.UseSqlServer(_connection);

			optionsBuilder.ReplaceService<IModelCacheKeyFactory, SnapshotModelCacheKeyFactory>();
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			var entity = modelBuilder.Entity<SnapshotStorageRow>();
			entity.ToTable(_options.TableName, _options.SchemaName);
			entity.HasKey(x => x.Id);
			entity.Property(x => x.Id).HasMaxLength(450);
			entity.Property(x => x.AggregateType).HasMaxLength(450);
			entity.Property(x => x.Payload).HasColumnType("json");
			entity.Property(x => x.TenantId).HasMaxLength(450);

			// A global filter rather than a per-query predicate: the snapshot store is the queryable
			// read model, so a query that forgets the tenant returns another tenant's aggregates.
			// An unowned row is visible only to the system scope: treating it as visible to everyone
			// would turn a missed stamp on any write path into silent cross-tenant exposure.
			entity.HasQueryFilter(x => _crossesTenants || x.TenantId == _tenantId);
			entity.HasIndex(x => x.AggregateType);
		}
	}

	sealed class SnapshotQueryDbContext<TAggregate> : DbContext, ISnapshotModelCacheKeySource
		where TAggregate : class
	{
		readonly SqlServerClientOptions _options;
		readonly SqlConnection? _connection;

		// Captured once per context: a context is scoped to one operation, and EF only re-evaluates
		// plain context fields when it reuses the cached model.
		bool _crossesTenants = true;
		string? _tenantId;

		/// <summary>The tenant owning rows written through this context.</summary>
		public string? TenantId => _tenantId;

		void SetTenant(ICurrentTenant? currentTenant)
		{
			var scope = (currentTenant ?? new SystemCurrentTenant()).Current;
			_crossesTenants = scope.IsSystem;
			_tenantId = scope.TenantId;
		}

		public SnapshotQueryDbContext(SqlServerClientOptions options, ICurrentTenant? currentTenant = null)
		{
			_options = options;
			SetTenant(currentTenant);
		}

		public SnapshotQueryDbContext(
			SqlServerClientOptions options,
			SqlConnection connection,
			ICurrentTenant? currentTenant = null
		)
		{
			_options = options;
			_connection = connection;
			SetTenant(currentTenant);
		}

		public DbSet<SnapshotQueryRow<TAggregate>> Snapshots => Set<SnapshotQueryRow<TAggregate>>();

		public object CreateModelCacheKey(bool designTime) =>
			(typeof(SnapshotQueryDbContext<>), typeof(TAggregate), _options.SchemaName, _options.TableName, designTime);

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
		{
			SnapshotModelMapper.RegisterScalarValueObjectConversions(configurationBuilder, typeof(TAggregate));
		}

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			if (_connection is null)
				optionsBuilder.UseSqlServer(_options.ConnectionString);
			else
				optionsBuilder.UseSqlServer(_connection);

			optionsBuilder.ReplaceService<IModelCacheKeyFactory, SnapshotModelCacheKeyFactory>();
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			// Snapshot payloads are value-object graphs whose members are init-only (or value-object
			// struct members). EF's default PreferField access mode writes those members through their
			// backing fields - readonly for readonly structs - and the JSON complex-type materializer
			// cannot rebuild such an assignment while fixing up a value-type member, which fails snapshot
			// query compilation with "Expression must be writeable".
			// PreferProperty writes members through their accessors (init included) and only falls back
			// to the backing field when no setter exists.
			modelBuilder.UsePropertyAccessMode(PropertyAccessMode.PreferProperty);

			var entity = modelBuilder.Entity<SnapshotQueryRow<TAggregate>>();
			entity.ToTable(_options.TableName, _options.SchemaName);
			entity.HasKey(static x => x.Id);
			entity.Property(static x => x.Id).HasMaxLength(450);
			entity.Property(static x => x.AggregateType).HasMaxLength(450);
			entity.HasIndex(static x => x.AggregateType);
			entity.Property(x => x.TenantId).HasMaxLength(450);

			// A global filter rather than a per-query predicate: the snapshot store is the queryable
			// read model, so a query that forgets the tenant returns another tenant's aggregates.
			// An unowned row is visible only to the system scope: treating it as visible to everyone
			// would turn a missed stamp on any write path into silent cross-tenant exposure.
			entity.HasQueryFilter(x => _crossesTenants || x.TenantId == _tenantId);
			SnapshotModelMapper.ValidateAggregatePayloadShape(typeof(TAggregate));
			entity.ComplexProperty(
				static x => x.Payload,
				static payload =>
				{
					payload.ToJson();
					SnapshotModelMapper.ConfigureGraph(SnapshotGraphBuilder.For(payload), typeof(TAggregate));
				}
			);
		}
	}

	async Task EnsureTableIfEnabledAsync(CancellationToken cancellationToken)
	{
		if (!_options.AutoCreateTable || IsTableEnsured())
			return;

		var tableLock = EnsureTableLocks.GetOrAdd(_tableEnsureKey, static _ => new SemaphoreSlim(1, 1));
		await tableLock.WaitAsync(cancellationToken);
		try
		{
			if (IsTableEnsured())
				return;

			try
			{
				await using var context = CreateStorageContext();
				await CreateStorageTablesWithEfAsync(context, cancellationToken);
				await using SqlConnection connection = new(_options.ConnectionString);
				await connection.OpenAsync(cancellationToken);
				await SqlServerJsonIndexSchemaManager.ApplyAsync(
					connection,
					transaction: null,
					_options.SchemaName,
					_options.TableName,
					_options.JsonIndexOptions,
					SupportedSnapshotIncludeColumns,
					cancellationToken
				);
			}
			catch (Exception ex) when (IsDuplicateTableCreateError(ex, _options.TableName))
			{
				// Another writer won the create race for the same table.
			}

			MarkTableEnsured();
		}
		finally
		{
			tableLock.Release();
		}
	}

	async Task EnsureTableIfEnabledAsync(
		SqlConnection connection,
		SqlTransaction? transaction,
		CancellationToken cancellationToken
	)
	{
		if (!_options.AutoCreateTable || IsTableEnsured())
			return;

		var tableLock = EnsureTableLocks.GetOrAdd(_tableEnsureKey, static _ => new SemaphoreSlim(1, 1));
		await tableLock.WaitAsync(cancellationToken);
		try
		{
			if (IsTableEnsured())
				return;

			try
			{
				await using var context = CreateStorageContext(connection);
				if (transaction is not null)
					await context.Database.UseTransactionAsync(transaction, cancellationToken);
				await CreateStorageTablesWithEfAsync(context, cancellationToken);
				await SqlServerJsonIndexSchemaManager.ApplyAsync(
					connection,
					transaction,
					_options.SchemaName,
					_options.TableName,
					_options.JsonIndexOptions,
					SupportedSnapshotIncludeColumns,
					cancellationToken
				);
			}
			catch (Exception ex) when (IsDuplicateTableCreateError(ex, _options.TableName))
			{
				// Another writer won the create race for the same table.
			}

			MarkTableEnsured();
		}
		finally
		{
			tableLock.Release();
		}
	}

	bool IsTableEnsured() => EnsuredTables.ContainsKey(_tableEnsureKey);

	void MarkTableEnsured() => EnsuredTables.TryAdd(_tableEnsureKey, 0);

	static async Task CreateStorageTablesWithEfAsync(DbContext context, CancellationToken cancellationToken)
	{
		var creator = context.GetService<IRelationalDatabaseCreator>();
		if (!await creator.ExistsAsync(cancellationToken))
			await creator.CreateAsync(cancellationToken);

		await creator.CreateTablesAsync(cancellationToken);
	}

	static bool IsDuplicateTableCreateError(Exception exception, string _)
	{
		for (var current = exception; current is not null; current = current.InnerException)
		{
			if (current.Message.Contains("already an object named", StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}
}
