using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Purview.EventSourcing.SqlServer.Events;

/// <summary>
/// Defines a schema and/or table name override for a specific aggregate type.
/// </summary>
/// <remarks>
/// The key in <see cref="SqlServerEventStoreOptions.AggregateTableOverrides"/> should match
/// the aggregate's <c>AggregateType</c> value (i.e., the class name with any trailing
/// "Aggregate" suffix removed, e.g. <c>"Order"</c> for <c>OrderAggregate</c>).
/// </remarks>
public sealed class SqlServerAggregateTableOverride
{
	/// <summary>
	/// Overrides the schema name for this aggregate type.
	/// When null, the global <see cref="SqlServerEventStoreOptions.SchemaName"/> is used.
	/// </summary>
	[RegularExpression(@"^[\w\-.]+$")]
	public string? SchemaName { get; set; }

	/// <summary>
	/// Overrides the table name for this aggregate type.
	/// When null, the global <see cref="SqlServerEventStoreOptions.TableName"/> is used.
	/// </summary>
	[RegularExpression(@"^[\w\-.]+$")]
	public string? TableName { get; set; }
}

/// <summary>
/// Options that configure the SQL Server event store.
/// </summary>
/// <remarks>
/// Bound from the <c>EventStore:SqlServer</c> configuration section; the connection string can also be resolved
/// from the configured connection-string names when it is not supplied directly.
/// </remarks>
public sealed class SqlServerEventStoreOptions
{
	/// <summary>
	/// The configuration section name used to bind <see cref="SqlServerEventStoreOptions"/>.
	/// </summary>
	public const string SqlServerEventStore = "EventStore:SqlServer";

	const bool DefaultRemoveDeletedFromCache = true;
	const int DefaultEventSuffixLength = 30;

	/// <summary>
	/// The connection string used to access the SQL Server (or Azure SQL) database.
	/// </summary>
	/// <remarks>Required; do not embed credentials in source control.</remarks>
	[Required]
	public string ConnectionString { get; set; } = default!;

	/// <summary>
	/// The name of the table that stores event-store rows.
	/// </summary>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression(@"^[\w\-.]+$")]
	public string TableName { get; set; } = "EventStoreEvents";

	/// <summary>
	/// The schema that owns the event-store table.
	/// </summary>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression(@"^[\w\-.]+$")]
	public string SchemaName { get; set; } = "dbo";

	/// <summary>
	/// When true, the table will be created if it doesn't exist.
	/// </summary>
	public bool AutoCreateTable { get; set; } = true;

	/// <summary>
	/// <para>When true, PAGE data compression is applied to the table and indices during auto-creation.</para>
	/// <para>This significantly reduces I/O for NVARCHAR(MAX) payload columns and improves read/write throughput.</para>
	/// <para>Available on SQL Server Enterprise Edition and all Azure SQL tiers.</para>
	/// </summary>
	/// <remarks>Only applies when <see cref="AutoCreateTable"/> is true and the table is being created for the first time.</remarks>
	public bool UseDataCompression { get; set; } = true;

	/// <summary>
	/// The command timeout, in seconds, applied to event-store database operations.
	/// </summary>
	[Range(1, 120000)]
	public int? TimeoutInSeconds { get; set; } = 60;

	/// <summary>
	/// The maximum number of events to save in a single operation.
	/// </summary>
	[Range(1, 10_000)]
	public int MaxEventCountOnSave { get; set; } = 1000;

	/// <summary>
	/// <para>Indicates if a deleted aggregate is removed from cache. Defaults to true.</para>
	/// <para>
	/// If true, when an aggregate is deleted, it is removed from the cache.
	/// Or in the case of a get, it is not placed in cache for future calls.
	/// </para>
	/// <para>If false, a deleted aggregate can be placed into cache.</para>
	/// </summary>
	[DefaultValue(DefaultRemoveDeletedFromCache)]
	public bool RemoveDeletedFromCache { get; set; } = DefaultRemoveDeletedFromCache;

	/// <summary>
	/// The length of the suffix when creating event records.
	/// </summary>
	/// <remarks>Changing this where data already exists will result in incomplete aggregates.</remarks>
	[Required]
	[Range(10, 100)]
	[DefaultValue(DefaultEventSuffixLength)]
	public int EventSuffixLength { get; set; } = DefaultEventSuffixLength;

	/// <summary>
	/// Gets/ sets a value indicating how the <see cref="IEventStore{T}"/>
	/// uses the <see cref="IDistributedCache"/> during it's operations. Defaults to <see cref="SnapshotCachingOptions.GetAndStore"/>.
	/// </summary>
	[DefaultValue(SnapshotCachingOptions.GetAndStore)]
	public SnapshotCachingOptions CacheMode { get; set; } = SnapshotCachingOptions.GetAndStore;

	/// <summary>
	/// The sliding-expiration duration used when storing aggregate snapshots in the distributed cache.
	/// </summary>
	public TimeSpan DefaultCacheSlidingDuration { get; set; } = TimeSpan.FromMinutes(60);

	/// <summary>
	/// <para>
	/// Gets/ sets a value indicating if a valid identifier from a <see cref="ClaimsPrincipal"/> is required when
	/// saving aggregates.
	/// </para>
	/// <para>
	/// Sets the <see cref="EventStoreOperationContext.RequiresValidPrincipalIdentifier"/> to this value
	/// on the <see cref="EventStoreOperationContext.Default"/> property.
	/// </para>
	/// </summary>
	/// <remarks>If true and <see cref="IPrincipalService.Identifier()"/> returns null or empty string, an exception is thrown.</remarks>
	[DefaultValue(true)]
	public bool RequiresValidPrincipalIdentifier { get; set; } = true;

	/// <summary>
	/// Gets or sets a value indicating whether a snapshot write that fails must fail the save.
	/// When true (the default), the snapshot is written atomically with the events in the same
	/// batch/transaction. When false, the snapshot is written best-effort after the events are
	/// committed and a snapshot failure is logged rather than failing the save. The per-operation
	/// <see cref="EventStoreOperationContext.RequireSnapshotWrite"/> overrides this value.
	/// </summary>
	[DefaultValue(true)]
	public bool RequireSnapshotWrite { get; set; } = true;

	/// <summary>
	/// Per-aggregate-type schema and/or table name overrides.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The key is the aggregate's <c>AggregateType</c> short name — typically the class name
	/// with any trailing <c>"Aggregate"</c> suffix removed (e.g. <c>"Order"</c> for
	/// <c>OrderAggregate</c>).
	/// </para>
	/// <para>
	/// Keys are matched case-insensitively. Null override values fall back to the
	/// global <see cref="SchemaName"/> / <see cref="TableName"/>.
	/// </para>
	/// <example>
	/// <code>
	/// // appsettings.json
	/// "EventStore:SqlServer": {
	///   "ConnectionString": "...",
	///   "AggregateTableOverrides": {
	///     "Order": { "SchemaName": "orders", "TableName": "EventStore" },
	///     "Inventory": { "SchemaName": "inventory" }
	///   }
	/// }
	/// </code>
	/// </example>
	/// </remarks>
	public Dictionary<string, SqlServerAggregateTableOverride
#pragma warning disable format
	> AggregateTableOverrides { get; init; } = [with(StringComparer.OrdinalIgnoreCase)];
#pragma warning restore format

	/// <summary>
	/// Configures runtime-managed SQL Server indexes over the event payload JSON column.
	/// </summary>
	[Required]
	public SqlServerJsonIndexOptions JsonIndexOptions { get; set; } = new();
}
