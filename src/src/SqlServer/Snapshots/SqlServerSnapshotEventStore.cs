using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.Internal;
using Purview.EventSourcing.SqlServer.Client;
using Purview.EventSourcing.SqlServer.Snapshots;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.SqlServer.Snapshot;

/// <summary>
/// SQL Server-backed snapshot event store that combines an underlying non-queryable event store with a
/// queryable snapshot model.
/// </summary>
/// <typeparam name="T">An <see cref="IAggregate"/> implementation.</typeparam>
/// <remarks>
/// Writes aggregate snapshots to a SQL Server (or Azure SQL) table and translates snapshot queries into
/// SQL/EF queries over the JSON payload column.
/// </remarks>
public sealed partial class SqlServerSnapshotEventStore<T>
	: ISqlServerSnapshotEventStore<T>,
		ITransactionalEventStore<T>
	where T : class, IAggregate, new()
{
	readonly IEventStoreCore<T> _eventStore;
	readonly IOptions<SqlServerSnapshotEventStoreOptions> _sqlServerEventStoreOptions;
	readonly ISqlServerSnapshotEventStoreTelemetry _telemetry;
	readonly ISnapshotStrategy<T> _snapshotStrategy;
	readonly ISnapshotStrategySelector? _snapshotStrategySelector;

	readonly SqlServerClient _sqlServerClient;

	readonly Type _aggregateType = typeof(T);
	readonly string _aggregateName;

	static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, string> AggregateTypeNames = new();

	/// <summary>
	/// Creates a new <see cref="SqlServerSnapshotEventStore{T}"/> instance.
	/// </summary>
	/// <param name="eventStore">The underlying non-queryable event store used for aggregate reads and writes.</param>
	/// <param name="sqlServerEventStoreOptions">The options that configure the snapshot store.</param>
	/// <param name="telemetry">The telemetry contract used to emit activities, metrics, and logs.</param>
	/// <param name="snapshotStrategy">Optional strategy that decides when an aggregate should be snapshotted; defaults to <see cref="AlwaysSnapshotStrategy{T}"/>.</param>
	/// <param name="snapshotStrategySelector">Optional selector used to resolve a snapshot strategy for a given aggregate.</param>
	/// <param name="currentTenant">Optional tenant scope applied to snapshot reads and writes.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="eventStore"/>, <paramref name="sqlServerEventStoreOptions"/>, or <paramref name="telemetry"/> is <see langword="null"/>.
	/// </exception>
	public SqlServerSnapshotEventStore(
		// Explicitly request a non-queryable event store.
		INonQueryableEventStore<T> eventStore,
		IOptions<SqlServerSnapshotEventStoreOptions> sqlServerEventStoreOptions,
		ISqlServerSnapshotEventStoreTelemetry telemetry,
		ISnapshotStrategy<T>? snapshotStrategy = null,
		ISnapshotStrategySelector? snapshotStrategySelector = null,
		ICurrentTenant? currentTenant = null
	)
	{
		_eventStore = eventStore;
		_sqlServerEventStoreOptions = sqlServerEventStoreOptions;
		_telemetry = telemetry;
		_snapshotStrategy = snapshotStrategy ?? new AlwaysSnapshotStrategy<T>();
		_snapshotStrategySelector = snapshotStrategySelector;

		_aggregateName = TypeNameHelper.GetName(_aggregateType, "Aggregate");

		var clientOptions = ResolveClientOptions(_sqlServerEventStoreOptions.Value, _aggregateName);
		_sqlServerClient = new(clientOptions, currentTenant);
	}

	/// <summary>
	/// Merges global options with any per-aggregate-type table override.
	/// </summary>
	static SqlServerClientOptions ResolveClientOptions(SqlServerSnapshotEventStoreOptions options, string aggregateName)
	{
		var schema = options.SchemaName;
		var table = options.TableName;

		if (options.AggregateTableOverrides.TryGetValue(aggregateName, out var ovr) && ovr is not null)
		{
			schema = ovr.SchemaName ?? schema;
			table = ovr.TableName ?? table;
		}

		return new(options.ConnectionString, options.UseDataCompression)
		{
			TableName = table,
			SchemaName = schema,
			AutoCreateTable = options.AutoCreateTable,
			JsonIndexOptions = options.JsonIndexOptions,
		};
	}

	/// <summary>
	/// This will upsert the aggregate in the snapshot store.
	/// <strong>Note</strong> if the aggregate has unsaved events, it will first save those
	/// events to the event store before upserting the snapshot to ensure consistency.
	/// </summary>
	/// <param name="aggregate"></param>
	/// <param name="cancellationToken"></param>
	public async Task SnapshotAsync(T aggregate, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		if (aggregate.HasUnsavedEvents())
			await _eventStore.SaveAsync(aggregate, cancellationToken);

		_telemetry.SnapshotSaveStart(aggregate.Details.Id, _aggregateName);
		using var activity = _telemetry.SnapshotSave(aggregate.Details.Id, _aggregateName);

		try
		{
			var result = await _sqlServerClient.UpsertAsync(
				aggregate,
				aggregate.Details.Id,
				GetAggregateTypeName(),
				cancellationToken
			);
			if (result)
			{
				_telemetry.SnapshotSaveComplete(aggregate.Details.Id, _aggregateName);
			}
		}
		catch (Exception ex)
		{
			_telemetry.SnapshotSaveFailed(aggregate.Details.Id, _aggregateName, ex);
			throw;
		}
	}

	string GetAggregateTypeName() => AggregateTypeNames.GetOrAdd(_aggregateType, _ => new T().AggregateType);
}
