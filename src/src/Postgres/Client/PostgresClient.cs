using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Purview.EventSourcing.Aggregates;
using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Postgres.Client;

sealed partial class PostgresClient
{
	static readonly ConcurrentDictionary<string, SemaphoreSlim> EnsureTableLocks = new(StringComparer.Ordinal);
	static readonly ConcurrentDictionary<string, byte> EnsuredTables = new(StringComparer.Ordinal);
	static readonly HashSet<string> SupportedSnapshotIncludeColumns = ["Id", "AggregateType", "Payload"];

	readonly PostgresClientOptions _options;
	readonly string _tableEnsureKey;

	public PostgresClient(PostgresClientOptions options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_tableEnsureKey = $"{_options.ConnectionString}|{_options.SchemaName}|{_options.TableName}";

		ValidateIdentifier(_options.SchemaName);
		ValidateIdentifier(_options.TableName);
		PostgresJsonIndexSchemaManager.ValidateOrThrow(
			_options.JsonIndexOptions,
			_options.SchemaName,
			_options.TableName,
			SupportedSnapshotIncludeColumns,
			nameof(PostgresClientOptions.JsonIndexOptions)
		);
	}

	public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
	{
		if (!_options.AutoCreateTable)
			return;

		await EnsureTableIfEnabledAsync(cancellationToken);
	}

	public async Task EnsureTableExistsAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default)
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
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
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
			: RewriteAggregateTypePredicate(whereClause, aggregateType);

		var rowQuery = context.Snapshots.AsNoTracking().AsSplitQuery().Where(s => s.AggregateType == aggregateType);

		var aggregateQuery = rowQuery.OrderBy(s => s.Id).Select(s => s.Payload);
		if (normalizedWhereClause != null)
			aggregateQuery = aggregateQuery.Where(normalizedWhereClause);

		if (orderByClause != null)
		{
			aggregateQuery = orderByClause(aggregateQuery);
			EnsureSupportedOrderByExpression(aggregateQuery.Expression);
		}

		var page = aggregateQuery.Skip(Math.Max(0, skipCount)).Take(Math.Max(0, takeCount));
		return await page.ToListAsync(cancellationToken);
	}

	public async Task<List<T>> QueryByAggregateTypeJsonContainsAsync<T>(
		string aggregateType,
		string jsonFragment,
		int skipCount,
		int takeCount,
		CancellationToken cancellationToken = default
	)
		where T : class
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(jsonFragment);
		await using var context = CreateQueryContext<T>();
		await EnsureTableIfEnabledAsync(cancellationToken);

		var sql = $"""
			SELECT "Id", "AggregateType", "Payload"
			FROM {QuoteIdentifier(_options.SchemaName)}.{QuoteIdentifier(_options.TableName)}
			WHERE "AggregateType" = @aggregateType AND "Payload" @> CAST(@jsonFragment AS jsonb)
			""";
		NpgsqlParameter<string> aggregateTypeParameter = new("aggregateType", aggregateType);
		NpgsqlParameter<string> jsonFragmentParameter = new("jsonFragment", jsonFragment);

		var query = context
			.Snapshots.FromSqlRaw(sql, aggregateTypeParameter, jsonFragmentParameter)
			.AsNoTracking()
			.OrderBy(static s => s.Id)
			.Select(static s => s.Payload)
			.Skip(Math.Max(0, skipCount))
			.Take(Math.Max(0, takeCount));

		return await query.ToListAsync(cancellationToken);
	}

	public async Task<List<T>> QueryByAggregateTypeJsonExistsAsync<T>(
		string aggregateType,
		string key,
		int skipCount,
		int takeCount,
		CancellationToken cancellationToken = default
	)
		where T : class
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		await using var context = CreateQueryContext<T>();
		await EnsureTableIfEnabledAsync(cancellationToken);

		var sql = $"""
			SELECT "Id", "AggregateType", "Payload"
			FROM {QuoteIdentifier(_options.SchemaName)}.{QuoteIdentifier(_options.TableName)}
			WHERE "AggregateType" = @aggregateType AND "Payload" ? @payloadKey
			""";
		NpgsqlParameter<string> aggregateTypeParameter = new("aggregateType", aggregateType);
		NpgsqlParameter<string> keyParameter = new("payloadKey", key);

		var query = context
			.Snapshots.FromSqlRaw(sql, aggregateTypeParameter, keyParameter)
			.AsNoTracking()
			.OrderBy(static s => s.Id)
			.Select(static s => s.Payload)
			.Skip(Math.Max(0, skipCount))
			.Take(Math.Max(0, takeCount));

		return await query.ToListAsync(cancellationToken);
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
		SnapshotQueryDBContext<T> context,
		string aggregateType,
		Expression<Func<T, bool>>? whereClause
	)
		where T : class
	{
		var query = context
			.Snapshots.AsNoTracking()
			.Where(s => s.AggregateType == aggregateType)
			.Select(s => s.Payload);

		return whereClause is null ? query : query.Where(RewriteAggregateTypePredicate(whereClause, aggregateType));
	}

	static async Task<bool> UpsertAsync<T>(
		T aggregate,
		string id,
		string aggregateType,
		SnapshotQueryDBContext<T> context,
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
		};

		if (exists)
			context.Update(entity);
		else
			context.Add(entity);

		return await context.SaveChangesAsync(cancellationToken) > 0;
	}

	SnapshotStorageDBContext CreateStorageContext() => new(_options);

	SnapshotStorageDBContext CreateStorageContext(NpgsqlConnection connection) => new(_options, connection);

	SnapshotQueryDBContext<T> CreateQueryContext<T>()
		where T : class => new(_options);

	SnapshotQueryDBContext<T> CreateQueryContext<T>(NpgsqlConnection connection)
		where T : class => new(_options, connection);

	static void ValidateIdentifier(string identifier)
	{
		if (string.IsNullOrWhiteSpace(identifier))
			throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));

		if (!IdentifierRegex().IsMatch(identifier))
			throw new ArgumentException($"Identifier '{identifier}' contains invalid characters.", nameof(identifier));
	}

	static string QuoteIdentifier(string identifier) => $"\"{identifier}\"";

	[GeneratedRegex(@"^[\w\-\.]+$")]
	private static partial Regex IdentifierRegex();

	sealed class SnapshotStorageDBContext : DbContext
	{
		readonly PostgresClientOptions _options;
		readonly NpgsqlConnection? _connection;

		public SnapshotStorageDBContext(PostgresClientOptions options)
		{
			_options = options;
		}

		public SnapshotStorageDBContext(PostgresClientOptions options, NpgsqlConnection connection)
		{
			_options = options;
			_connection = connection;
		}

		public DbSet<SnapshotStorageRow> Snapshots => Set<SnapshotStorageRow>();

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			if (_connection is null)
				optionsBuilder.UseNpgsql(_options.ConnectionString);
			else
				optionsBuilder.UseNpgsql(_connection);

			optionsBuilder.ReplaceService<IModelCacheKeyFactory, SnapshotModelCacheKeyFactory>();
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			var entity = modelBuilder.Entity<SnapshotStorageRow>();
			entity.ToTable(_options.TableName, _options.SchemaName);
			entity.HasKey(x => x.Id);
			entity.Property(x => x.Id).HasMaxLength(450);
			entity.Property(x => x.AggregateType).HasMaxLength(450);
			entity.Property(x => x.Payload).HasColumnType("jsonb");
			entity.HasIndex(x => x.AggregateType);
		}
	}

	sealed class SnapshotQueryDBContext<TAggregate> : DbContext
		where TAggregate : class
	{
		readonly PostgresClientOptions _options;
		readonly NpgsqlConnection? _connection;

		public SnapshotQueryDBContext(PostgresClientOptions options)
		{
			_options = options;
		}

		public SnapshotQueryDBContext(PostgresClientOptions options, NpgsqlConnection connection)
		{
			_options = options;
			_connection = connection;
		}

		public DbSet<SnapshotQueryRow<TAggregate>> Snapshots => Set<SnapshotQueryRow<TAggregate>>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
		{
			RegisterScalarValueObjectConversions(configurationBuilder, typeof(TAggregate));
		}

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			if (_connection is null)
				optionsBuilder.UseNpgsql(_options.ConnectionString);
			else
				optionsBuilder.UseNpgsql(_connection);

			optionsBuilder.ReplaceService<IModelCacheKeyFactory, SnapshotModelCacheKeyFactory>();
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			var entity = modelBuilder.Entity<SnapshotQueryRow<TAggregate>>();
			entity.ToTable(_options.TableName, _options.SchemaName);
			entity.HasKey(static x => x.Id);
			entity.Property(static x => x.Id).HasMaxLength(450);
			entity.Property(static x => x.AggregateType).HasMaxLength(450);
			entity.HasIndex(static x => x.AggregateType);
			ValidateAggregatePayloadShape(typeof(TAggregate));
			entity.ComplexProperty(
				static x => x.Payload,
				static payload =>
				{
					payload.ToJson();
					ConfigureComplexGraph(payload, typeof(TAggregate));
				}
			);
		}
	}

	static void RegisterScalarValueObjectConversions(ModelConfigurationBuilder configurationBuilder, Type rootType)
	{
		HashSet<Type> visited = [];
		RegisterScalarValueObjectConversionsRecursive(configurationBuilder, rootType, visited);
	}

	static void RegisterScalarValueObjectConversionsRecursive(
		ModelConfigurationBuilder configurationBuilder,
		Type type,
		HashSet<Type> visited
	)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (!visited.Add(type))
			return;

		var scalarAttribute = type.GetCustomAttribute<ScalarAttribute>();
		if (scalarAttribute is not null)
		{
			RegisterScalarValueObjectConversion(configurationBuilder, type, scalarAttribute);
			return;
		}

		if (!ShouldInspectType(type))
			return;

		foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
		{
			var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

			if (propertyType == typeof(string) || propertyType == typeof(byte[]))
				continue;

			if (TryGetEnumerableElementType(propertyType, out var elementType))
			{
				RegisterScalarValueObjectConversionsRecursive(configurationBuilder, elementType, visited);
				continue;
			}

			RegisterScalarValueObjectConversionsRecursive(configurationBuilder, propertyType, visited);
		}
	}

	static void RegisterScalarValueObjectConversion(
		ModelConfigurationBuilder configurationBuilder,
		Type scalarType,
		ScalarAttribute scalarAttribute
	)
	{
		var scalarProperty =
			scalarType.GetProperty(scalarAttribute.PropertyName, BindingFlags.Instance | BindingFlags.Public)
			?? throw new InvalidOperationException(
				$"'{scalarType.Name}' missing scalar property '{scalarAttribute.PropertyName}'."
			);

		var scalarPropertyType = scalarProperty.PropertyType;
		var converterType = IsPrimitiveLike(scalarPropertyType)
			? typeof(ScalarValueConverter<,>).MakeGenericType(scalarType, scalarPropertyType)
			: typeof(JsonScalarValueConverter<,>).MakeGenericType(scalarType, scalarPropertyType);
		configurationBuilder.Properties(scalarType, builder => builder.HaveConversion(converterType));
	}

	static void ConfigureComplexGraph(ComplexPropertyBuilder builder, Type type)
	{
		HashSet<Type> visited = [];
		ConfigureComplexGraphRecursive(builder, type, visited);
	}

	static void ConfigureComplexGraphRecursive(ComplexPropertyBuilder builder, Type type, HashSet<Type> visited)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (!ShouldInspectType(type) || !visited.Add(type))
			return;

		foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
		{
			if (ShouldSkipJsonProperty(type, property))
			{
				builder.Ignore(property.Name);
				continue;
			}

			var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

			if (propertyType == typeof(string) || propertyType == typeof(byte[]))
			{
				MapReadOnlyConstructorBoundProperty(builder, type, property, propertyType);
				continue;
			}

			if (propertyType.GetCustomAttribute<ScalarAttribute>() is not null)
			{
				MapReadOnlyConstructorBoundProperty(builder, type, property, propertyType);
				continue;
			}

			if (HasEfOpaqueAttribute(property))
			{
				MapOpaqueJsonProperty(builder, propertyType, property.Name);
				continue;
			}

			if (TryGetEnumerableElementType(propertyType, out var elementType))
			{
				if (!IsEventStoreCollectionType(propertyType))
					throw CreateUnsupportedCollectionTypeException(type, property, propertyType);

				if (IsStructValueObjectType(elementType))
				{
					MapCollectionAsJsonProperty(builder, propertyType, property.Name);
					continue;
				}

				if (IsPrimitiveLike(elementType) || elementType.GetCustomAttribute<ScalarAttribute>() is not null)
					builder.PrimitiveCollection(propertyType, property.Name);
				else if (ShouldInspectType(elementType))
					builder.ComplexCollection(
						propertyType,
						property.Name,
						nested => ConfigureComplexGraphRecursive(nested, elementType, [.. visited])
					);
				else
					throw CreateUnsupportedShapeException(type, property);

				continue;
			}

			if (IsDictionaryLikeType(propertyType) || IsJsonConvertibleCollectionType(propertyType))
			{
				MapJsonProperty(builder, propertyType, property.Name);
				continue;
			}

			if (IsCollectionLikeType(propertyType))
				throw CreateUnsupportedCollectionTypeException(type, property, propertyType);

			if (ShouldOwnType(propertyType))
			{
				builder.ComplexProperty(
					propertyType,
					property.Name,
					nested => ConfigureComplexGraphRecursive(nested, propertyType, [.. visited])
				);
				continue;
			}

			if (IsPrimitiveLike(propertyType))
			{
				MapReadOnlyConstructorBoundProperty(builder, type, property, propertyType);
				continue;
			}

			throw CreateUnsupportedShapeException(type, property);
		}
	}

	static void ConfigureComplexGraphRecursive(ComplexCollectionBuilder builder, Type type, HashSet<Type> visited)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (!ShouldInspectType(type) || !visited.Add(type))
			return;

		foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
		{
			if (ShouldSkipJsonProperty(type, property))
			{
				builder.Ignore(property.Name);
				continue;
			}

			var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

			if (propertyType == typeof(string) || propertyType == typeof(byte[]))
			{
				MapReadOnlyConstructorBoundProperty(builder, type, property, propertyType);
				continue;
			}

			if (propertyType.GetCustomAttribute<ScalarAttribute>() is not null)
			{
				MapReadOnlyConstructorBoundProperty(builder, type, property, propertyType);
				continue;
			}

			if (HasEfOpaqueAttribute(property))
			{
				MapOpaqueJsonProperty(builder, propertyType, property.Name);
				continue;
			}

			if (TryGetEnumerableElementType(propertyType, out var elementType))
			{
				if (!IsEventStoreCollectionType(propertyType))
					throw CreateUnsupportedCollectionTypeException(type, property, propertyType);

				if (IsStructValueObjectType(elementType))
				{
					MapCollectionAsJsonProperty(builder, propertyType, property.Name);
					continue;
				}

				if (IsPrimitiveLike(elementType) || elementType.GetCustomAttribute<ScalarAttribute>() is not null)
					builder.PrimitiveCollection(propertyType, property.Name);
				else if (ShouldInspectType(elementType))
					builder.ComplexCollection(
						propertyType,
						property.Name,
						nested => ConfigureComplexGraphRecursive(nested, elementType, [.. visited])
					);
				else
					throw CreateUnsupportedShapeException(type, property);

				continue;
			}

			if (IsDictionaryLikeType(propertyType) || IsJsonConvertibleCollectionType(propertyType))
			{
				MapJsonProperty(builder, propertyType, property.Name);
				continue;
			}

			if (IsCollectionLikeType(propertyType))
				throw CreateUnsupportedCollectionTypeException(type, property, propertyType);

			if (ShouldOwnType(propertyType))
			{
				builder.ComplexProperty(
					propertyType,
					property.Name,
					nested => ConfigureComplexGraphRecursive(nested, propertyType, [.. visited])
				);
				continue;
			}

			if (IsPrimitiveLike(propertyType))
			{
				MapReadOnlyConstructorBoundProperty(builder, type, property, propertyType);
				continue;
			}

			throw CreateUnsupportedShapeException(type, property);
		}
	}

	static void MapReadOnlyConstructorBoundProperty(
		ComplexPropertyBuilder builder,
		Type containingType,
		PropertyInfo property,
		Type propertyType
	)
	{
		if (property.GetSetMethod(true) is null && HasBindableConstructorParameter(containingType, property))
			builder.Property(propertyType, property.Name);
	}

	static void MapReadOnlyConstructorBoundProperty(
		ComplexCollectionBuilder builder,
		Type containingType,
		PropertyInfo property,
		Type propertyType
	)
	{
		if (property.GetSetMethod(true) is null && HasBindableConstructorParameter(containingType, property))
			builder.Property(propertyType, property.Name);
	}

	static void ValidateAggregatePayloadShape(Type type)
	{
		HashSet<Type> visited = [];
		ValidateAggregatePayloadShapeRecursive(type, visited);
	}

	static void ValidateAggregatePayloadShapeRecursive(Type type, HashSet<Type> visited)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (!ShouldInspectType(type) || !visited.Add(type))
			return;

		foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
		{
			if (ShouldSkipJsonProperty(type, property))
				continue;

			var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

			if (propertyType == typeof(string) || propertyType == typeof(byte[]))
				continue;

			if (propertyType.GetCustomAttribute<ScalarAttribute>() is not null)
				continue;

			if (HasEfOpaqueAttribute(property))
				continue;

			if (TryGetEnumerableElementType(propertyType, out var elementType))
			{
				if (!IsEventStoreCollectionType(propertyType))
					throw CreateUnsupportedCollectionTypeException(type, property, propertyType);

				if (
					!IsPrimitiveLike(elementType)
					&& elementType.GetCustomAttribute<ScalarAttribute>() is null
					&& !IsStructValueObjectType(elementType)
				)
				{
					if (!ShouldInspectType(elementType))
						throw CreateUnsupportedShapeException(type, property);

					ValidateAggregatePayloadShapeRecursive(elementType, visited);
				}

				continue;
			}

			if (IsReadOnlyCollectionType(propertyType))
				throw CreateUnsupportedCollectionTypeException(type, property, propertyType);

			if (IsDictionaryLikeType(propertyType) || IsJsonConvertibleCollectionType(propertyType))
				continue;

			if (IsCollectionLikeType(propertyType))
				throw CreateUnsupportedCollectionTypeException(type, property, propertyType);

			if (ShouldOwnType(propertyType))
			{
				ValidateAggregatePayloadShapeRecursive(propertyType, visited);
				continue;
			}

			if (!IsPrimitiveLike(propertyType))
				throw CreateUnsupportedShapeException(type, property);
		}
	}

	static bool IsStructValueObjectType(Type type) =>
		type.IsValueType && type.GetCustomAttribute<ValueObjectAttribute>() is not null;

	static bool HasEfOpaqueAttribute(MemberInfo member) =>
		member.CustomAttributes.Any(attribute =>
			attribute.AttributeType.FullName == "Purview.EventSourcing.EntityFrameworkCore.EfOpaqueAttribute"
		);

	static void MapOpaqueJsonProperty(ComplexPropertyBuilder builder, Type propertyType, string propertyName)
	{
		var propertyBuilder = builder.Property(propertyType, propertyName);
		propertyBuilder.HasConversion(typeof(OpaqueJsonValueConverter<>).MakeGenericType(propertyType));
	}

	static void MapOpaqueJsonProperty(ComplexCollectionBuilder builder, Type propertyType, string propertyName)
	{
		var propertyBuilder = builder.Property(propertyType, propertyName);
		propertyBuilder.HasConversion(typeof(OpaqueJsonValueConverter<>).MakeGenericType(propertyType));
	}

	static void MapCollectionAsJsonProperty(ComplexPropertyBuilder builder, Type propertyType, string propertyName)
	{
		var propertyBuilder = builder.Property(propertyType, propertyName);
		var converterType = typeof(JsonValueObjectCollectionConverter<>).MakeGenericType(propertyType);
		propertyBuilder.HasConversion(converterType);
	}

	static void MapCollectionAsJsonProperty(ComplexCollectionBuilder builder, Type propertyType, string propertyName)
	{
		var propertyBuilder = builder.Property(propertyType, propertyName);
		var converterType = typeof(JsonValueObjectCollectionConverter<>).MakeGenericType(propertyType);
		propertyBuilder.HasConversion(converterType);
	}

	static void MapJsonProperty(ComplexPropertyBuilder builder, Type propertyType, string propertyName)
	{
		var propertyBuilder = builder.Property(propertyType, propertyName);
		var converterType = typeof(JsonValueObjectCollectionConverter<>).MakeGenericType(propertyType);
		propertyBuilder.HasConversion(converterType);
	}

	static void MapJsonProperty(ComplexCollectionBuilder builder, Type propertyType, string propertyName)
	{
		var propertyBuilder = builder.Property(propertyType, propertyName);
		var converterType = typeof(JsonValueObjectCollectionConverter<>).MakeGenericType(propertyType);
		propertyBuilder.HasConversion(converterType);
	}

	static bool ShouldOwnType(Type type) =>
		!type.IsPrimitive
		&& !type.IsEnum
		&& type != typeof(string)
		&& type != typeof(decimal)
		&& type != typeof(Guid)
		&& type != typeof(DateTime)
		&& type != typeof(DateTimeOffset)
		&& type != typeof(DateOnly)
		&& type != typeof(TimeOnly)
		&& type != typeof(TimeSpan)
		&& (type.Namespace is null || !type.Namespace.StartsWith("System", StringComparison.Ordinal))
		&& (type.IsClass || (type.IsValueType && !type.IsPrimitive && !type.IsEnum));

	static bool ShouldInspectType(Type type) =>
		!type.IsPrimitive
		&& !type.IsEnum
		&& type != typeof(string)
		&& type != typeof(decimal)
		&& type != typeof(Guid)
		&& type != typeof(DateTime)
		&& type != typeof(DateTimeOffset)
		&& type != typeof(DateOnly)
		&& type != typeof(TimeOnly)
		&& type != typeof(TimeSpan)
		&& (type.Namespace is null || !type.Namespace.StartsWith("System", StringComparison.Ordinal));

	static bool ShouldSkipJsonProperty(Type containingType, PropertyInfo property) =>
		property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() is not null
		|| property.GetGetMethod(true) is null
		|| (property.GetSetMethod(true) is null && !HasBindableConstructorParameter(containingType, property));

	static bool HasBindableConstructorParameter(Type containingType, PropertyInfo property)
	{
		var constructors = containingType.GetConstructors(
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
		);
		foreach (var constructor in constructors)
		{
			foreach (var parameter in constructor.GetParameters())
			{
				if (
					string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase)
					&& parameter.ParameterType == property.PropertyType
				)
					return true;
			}
		}

		return false;
	}

	static bool IsPrimitiveLike(Type type) =>
		type.IsPrimitive
		|| type.IsEnum
		|| type == typeof(string)
		|| type == typeof(decimal)
		|| type == typeof(Guid)
		|| type == typeof(Uri)
		|| type == typeof(DateTime)
		|| type == typeof(DateTimeOffset)
		|| type == typeof(DateOnly)
		|| type == typeof(TimeOnly)
		|| type == typeof(TimeSpan)
		|| type == typeof(byte[]);

	static bool TryGetEnumerableElementType(Type type, out Type elementType)
	{
		elementType = null!;

		if (!type.IsGenericType)
			return false;

		if (!IsEventStoreCollectionType(type))
			return false;

		elementType = type.GetGenericArguments()[0];
		return true;
	}

	static bool IsEventStoreCollectionType(Type type) =>
		type.IsGenericType
		&& type.GetGenericTypeDefinition() is var genericDefinition
		&& (genericDefinition == typeof(EventStoreList<>) || genericDefinition == typeof(EventStoreSet<>));

	static bool IsCollectionLikeType(Type type)
	{
		if (type == typeof(string) || type == typeof(byte[]))
			return false;

		if (type.IsArray)
			return true;

		if (!type.IsGenericType)
			return false;

		// Test for our own collection types or if the type implements IEnumerable<T> somewhere...
		return IsEventStoreCollectionType(type)
			|| type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
	}

	static bool IsReadOnlyCollectionType(Type type)
	{
		if (!type.IsGenericType)
			return false;

		var genericTypeDefinition = type.GetGenericTypeDefinition();
		return genericTypeDefinition == typeof(IReadOnlyList<>)
			|| genericTypeDefinition == typeof(IReadOnlyCollection<>);
	}

	static bool IsDictionaryLikeType(Type type)
	{
		if (!type.IsGenericType)
			return false;

		var genericTypeDefinition = type.GetGenericTypeDefinition();
		if (
			genericTypeDefinition == typeof(Dictionary<,>)
			|| genericTypeDefinition == typeof(IDictionary<,>)
			|| genericTypeDefinition == typeof(IReadOnlyDictionary<,>)
		)
			return true;

		// Check if the type implements IDictionary<TKey, TValue> or IReadOnlyDictionary<TKey, TValue> somewhere...
		return type.GetInterfaces()
			.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
	}

	static bool IsJsonConvertibleCollectionType(Type type)
	{
		if (type == typeof(string) || type == typeof(byte[]))
			return false;

		if (!type.IsGenericType || IsEventStoreCollectionType(type) || IsDictionaryLikeType(type))
			return false;

		if (type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
			return true;

		// Check if the type implements IEnumerable<T> somewhere...
		return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
	}

	static InvalidOperationException CreateUnsupportedShapeException(Type containingType, MemberInfo member) =>
		new(
			$"{containingType.Name}.{member.Name} cannot be mapped into the snapshot JSON payload. "
				+ "Supported members are primitive types, [Scalar] value objects, complex types, and EventStoreList<T>/EventStoreSet<T> collections of those shapes."
		);

	static InvalidOperationException CreateUnsupportedCollectionTypeException(
		Type containingType,
		MemberInfo member,
		Type collectionType
	) =>
		new(
			$"{containingType.Name}.{member.Name} uses unsupported collection type '{collectionType.Name}'. "
				+ "Collection and array members must use Purview.EventSourcing.EventStoreList<T> or Purview.EventSourcing.EventStoreSet<T>."
		);

	static void EnsureSupportedOrderByExpression(Expression queryExpression) =>
		new UnsupportedScalarValueOrderByDetector().Visit(queryExpression);

	static Expression<Func<T, bool>> RewriteAggregateTypePredicate<T>(
		Expression<Func<T, bool>> whereClause,
		string aggregateType
	)
		where T : class
	{
		AggregateTypeExpressionVisitor aggregateTypeVisitor = new(aggregateType);
		var rewritten = (Expression<Func<T, bool>>)aggregateTypeVisitor.Visit(whereClause);
		InvariantStringMethodNormalizationVisitor invariantCasingVisitor = new();
		rewritten = (Expression<Func<T, bool>>)invariantCasingVisitor.Visit(rewritten);
		ScalarValueMemberAccessPredicateVisitor scalarVisitor = new();
		return (Expression<Func<T, bool>>)scalarVisitor.Visit(rewritten);
	}

	sealed class AggregateTypeExpressionVisitor(string aggregateType) : ExpressionVisitor
	{
		readonly ConstantExpression _aggregateType = Expression.Constant(aggregateType, typeof(string));

		protected override Expression VisitMember(MemberExpression node) =>
			node.Member.Name == nameof(IAggregate.AggregateType)
			&& node.Expression is not null
			&& typeof(IAggregate).IsAssignableFrom(node.Expression.Type)
				? _aggregateType
				: base.VisitMember(node);
	}

	sealed class UnsupportedScalarValueOrderByDetector : ExpressionVisitor
	{
		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			if (
				node.Method.DeclaringType == typeof(Queryable)
				&& (
					node.Method.Name == nameof(Queryable.OrderBy)
					|| node.Method.Name == nameof(Queryable.OrderByDescending)
					|| node.Method.Name == nameof(Queryable.ThenBy)
					|| node.Method.Name == nameof(Queryable.ThenByDescending)
				)
			)
			{
				var lambda = (LambdaExpression)StripQuotes(node.Arguments[1]);
				new ScalarValueMemberAccessDetector().Visit(lambda.Body);
			}

			return base.VisitMethodCall(node);
		}

		static Expression StripQuotes(Expression expression)
		{
			while (expression.NodeType == ExpressionType.Quote)
				expression = ((UnaryExpression)expression).Operand;

			return expression;
		}
	}

	sealed class ScalarValueMemberAccessPredicateVisitor : ExpressionVisitor
	{
		protected override Expression VisitBinary(BinaryExpression node)
		{
			var visitedLeft = Visit(node.Left);
			var visitedRight = Visit(node.Right);

			return
				(node.NodeType == ExpressionType.Equal || node.NodeType == ExpressionType.NotEqual)
				&& TryRewriteScalarPrimitiveComparison(
					visitedLeft,
					visitedRight,
					out var rewrittenLeft,
					out var rewrittenRight
				)
				? Expression.MakeBinary(node.NodeType, rewrittenLeft, rewrittenRight)
				: node.Update(visitedLeft, node.Conversion, visitedRight);
		}

		protected override Expression VisitMember(MemberExpression node)
		{
			var visitedExpression = base.VisitMember(node);
			if (visitedExpression is not MemberExpression visited || visited.Expression is null)
				return visitedExpression;

			var scalarAttribute = visited.Expression.Type.GetCustomAttribute<ScalarAttribute>();
			if (scalarAttribute is null || visited.Member.Name != scalarAttribute.PropertyName)
				return visited;

			try
			{
				return Expression.Convert(visited.Expression, visited.Type);
			}
			catch (InvalidOperationException)
			{
				return visited;
			}
		}

		static bool TryRewriteScalarPrimitiveComparison(
			Expression left,
			Expression right,
			out Expression rewrittenLeft,
			out Expression rewrittenRight
		)
		{
			rewrittenLeft = left;
			rewrittenRight = right;

			if (
				TryGetScalarPropertyType(left.Type, out var leftScalarType)
				&& IsSameOrNullable(right.Type, leftScalarType)
			)
			{
				rewrittenLeft = Expression.Convert(left, right.Type);
				return true;
			}

			if (
				TryGetScalarPropertyType(right.Type, out var rightScalarType)
				&& IsSameOrNullable(left.Type, rightScalarType)
			)
			{
				rewrittenRight = Expression.Convert(right, left.Type);
				return true;
			}

			return false;
		}

		static bool TryGetScalarPropertyType(Type candidateType, out Type scalarPropertyType)
		{
			scalarPropertyType = null!;
			var scalarAttribute = candidateType.GetCustomAttribute<ScalarAttribute>();
			if (scalarAttribute is null)
				return false;

			var scalarProperty = candidateType.GetProperty(
				scalarAttribute.PropertyName,
				BindingFlags.Instance | BindingFlags.Public
			);
			if (scalarProperty is null)
				return false;

			scalarPropertyType = scalarProperty.PropertyType;
			return true;
		}

		static bool IsSameOrNullable(Type candidate, Type expected)
		{
			var underlyingType = Nullable.GetUnderlyingType(candidate);
			return candidate == expected || underlyingType == expected;
		}
	}

	sealed class InvariantStringMethodNormalizationVisitor : ExpressionVisitor
	{
		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			var visitedObject = Visit(node.Object);
			var visitedArguments = Visit(node.Arguments);

			if (node.Method.DeclaringType == typeof(string) && node.Arguments.Count == 0 && visitedObject is not null)
			{
				if (node.Method.Name == nameof(string.ToLowerInvariant))
					return Expression.Call(visitedObject, nameof(string.ToLower), Type.EmptyTypes);

				if (node.Method.Name == nameof(string.ToUpperInvariant))
					return Expression.Call(visitedObject, nameof(string.ToUpper), Type.EmptyTypes);
			}

			return node.Update(visitedObject, visitedArguments);
		}
	}

	sealed class ScalarValueMemberAccessDetector : ExpressionVisitor
	{
		protected override Expression VisitMember(MemberExpression node)
		{
			var visited = base.VisitMember(node);
			if (node.Expression is null)
				return visited;

			var scalarAttribute = node.Expression.Type.GetCustomAttribute<ScalarAttribute>();
			return scalarAttribute is not null && node.Member.Name == scalarAttribute.PropertyName
				? throw new InvalidOperationException(
					$"Ordering by '{node.Expression.Type.Name}.{node.Member.Name}' is not supported for SQL snapshot JSON queries. "
						+ "Order by the scalar value object property itself (for example: c => c.Name) instead of its inner scalar member."
				)
				: visited;
		}
	}

	sealed class SnapshotModelCacheKeyFactory : IModelCacheKeyFactory
	{
		public object Create(DbContext context, bool designTime)
		{
			var optionsField = context.GetType().GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic);
			if (optionsField?.GetValue(context) is PostgresClientOptions options)
			{
				if (
					context.GetType().IsGenericType
					&& context.GetType().GetGenericTypeDefinition() == typeof(SnapshotQueryDBContext<>)
				)
				{
					var aggregateType = context.GetType().GenericTypeArguments[0];
					return (context.GetType(), aggregateType, options.SchemaName, options.TableName, designTime);
				}

				return (context.GetType(), options.SchemaName, options.TableName, designTime);
			}

			return (context.GetType(), designTime);
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
				await using NpgsqlConnection connection = new(_options.ConnectionString);
				await connection.OpenAsync(cancellationToken);
				await PostgresJsonIndexSchemaManager.ApplyAsync(
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
		NpgsqlConnection connection,
		NpgsqlTransaction? transaction,
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
				await PostgresJsonIndexSchemaManager.ApplyAsync(
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
