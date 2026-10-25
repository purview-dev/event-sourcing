using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.SqlServer.Events.EntityFramework;

/// <summary>
/// EF Core DbContext for the SQL Server event store.
/// Provides model-first design with migrations for the event store table,
/// matching the same schema and indices as the ADO.NET auto-create path.
/// </summary>
/// <remarks>
/// Creates a new <see cref="EventStoreDbContext"/> with explicit schema and table names.
/// </remarks>
public class EventStoreDbContext(
	DbContextOptions<EventStoreDbContext> options,
	string schemaName,
	string tableName,
	ICurrentTenant? currentTenant = null
) : DbContext(options)
{
	readonly string _schemaName = schemaName;
	readonly string _tableName = tableName;

	// Captured once per context rather than read per query: a context is short-lived and scoped to one
	// operation, and EF only re-evaluates plain context fields when it reuses the cached model.
	readonly bool _crossesTenants = (currentTenant?.Current ?? TenantScope.System).IsSystem;
	readonly string? _tenantId = (currentTenant?.Current ?? TenantScope.System).TenantId;

	/// <summary>
	/// All event store rows (stream versions, events, idempotency markers, snapshots).
	/// </summary>
	public DbSet<EventStoreEntity> EventStoreEntities { get; set; } = default!;

	/// <summary>
	/// Creates a new <see cref="EventStoreDbContext"/> with the specified options.
	/// Schema and table names default to "dbo" and "EventStore".
	/// </summary>
	public EventStoreDbContext(DbContextOptions<EventStoreDbContext> options)
		: this(options, "dbo", "EventStore") { }

	///<inheritdoc/>
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ArgumentNullException.ThrowIfNull(modelBuilder);

		modelBuilder.Entity<EventStoreEntity>(entity =>
		{
			entity.ToTable(_tableName, _schemaName);

			entity.HasKey(e => e.Id);
			entity.Property(e => e.Id).HasMaxLength(450).IsRequired();
			entity.Property(e => e.EntityType).IsRequired();
			entity.Property(e => e.AggregateId).HasMaxLength(450).IsRequired();
			entity.Property(e => e.AggregateType).HasMaxLength(450).IsRequired();
			entity.Property(e => e.Version).HasDefaultValue(0).IsRequired();
			entity.Property(e => e.IsDeleted).HasDefaultValue(false).IsRequired();
			entity.Property(e => e.Payload).HasColumnType("json");
			entity.Property(e => e.EventType).HasMaxLength(450);
			entity.Property(e => e.IdempotencyId).HasMaxLength(450);
			entity.Property(e => e.SchemaVersion).HasDefaultValue(1).IsRequired();
			entity.Property(e => e.CorrelationId).HasMaxLength(450);
			entity.Property(e => e.CausationId).HasMaxLength(450);
			entity.Property(e => e.UserId).HasMaxLength(450);
			entity.Property(e => e.TenantId).HasMaxLength(450);

			// A global filter rather than a per-query predicate: a read that forgets the tenant is the
			// failure mode this exists to prevent, and EF applies this to every LINQ query on the set.
			// Both members are fields, so EF parameterises them and one cached model serves all tenants.
			entity.HasQueryFilter(e => _crossesTenants || e.TenantId == _tenantId);
			entity.Property(e => e.Timestamp).HasDefaultValueSql("SYSUTCDATETIME()").IsRequired();

			// Covers: GetByAggregateIdAndEntityType, GetIdempotencyMarkers, DeleteByAggregateId
			entity
				.HasIndex(e => new
				{
					e.AggregateId,
					e.AggregateType,
					e.EntityType,
				})
				.HasDatabaseName($"IX_{_tableName}_AggregateId_EntityType")
				.IncludeProperties(e => new { e.Version, e.IsDeleted });

			// Covers: GetEventRange (AggregateId + AggregateType + EntityType=1 + Version range, ORDER BY Version)
			entity
				.HasIndex(e => new
				{
					e.AggregateId,
					e.AggregateType,
					e.Version,
				})
				.HasDatabaseName($"IX_{_tableName}_EventRange")
				.HasFilter("[EntityType] = 1")
				.IncludeProperties(e => new
				{
					e.Payload,
					e.EventType,
					e.IdempotencyId,
					e.SchemaVersion,
					e.CorrelationId,
					e.CausationId,
					e.UserId,
					e.Timestamp,
				});

			// Covers: GetAggregateIdsAsync (AggregateType + EntityType + optional IsDeleted filter)
			entity
				.HasIndex(e => new
				{
					e.AggregateType,
					e.EntityType,
					e.IsDeleted,
				})
				.HasDatabaseName($"IX_{_tableName}_AggregateType_EntityType")
				.IncludeProperties(e => new { e.AggregateId });
		});
	}

	///<inheritdoc/>
	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		ArgumentNullException.ThrowIfNull(optionsBuilder);
		optionsBuilder.ReplaceService<IModelCacheKeyFactory, EventStoreModelCacheKeyFactory>();
	}

	sealed class EventStoreModelCacheKeyFactory : IModelCacheKeyFactory
	{
		public object Create(DbContext context, bool designTime)
		{
			if (context is EventStoreDbContext eventStoreContext)
				return (context.GetType(), eventStoreContext._schemaName, eventStoreContext._tableName, designTime);

			return (context.GetType(), designTime);
		}
	}
}
