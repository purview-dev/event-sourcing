using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.Internal;
using Purview.EventSourcing.Postgres.Client;
using Purview.EventSourcing.Postgres.Snapshots;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Postgres.Snapshot;

/// <summary>
/// A PostgreSQL queryable snapshot event store that combines an event store with snapshot querying for <typeparamref name="T"/> aggregates.
/// </summary>
/// <typeparam name="T">An <see cref="IAggregate"/> implementation.</typeparam>
/// <remarks>
/// Aggregates are saved through the enlisted event store and snapshots are upserted into the
/// PostgreSQL snapshot table, enabling JSON payload querying via <see cref="IPostgresSnapshotEventStore{T}"/>.
/// </remarks>
public sealed partial class PostgresSnapshotEventStore<T> : IPostgresSnapshotEventStore<T>, ITransactionalEventStore<T>
	where T : class, IAggregate, new()
{
	readonly IEventStoreCore<T> _eventStore;
	readonly IOptions<PostgresSnapshotEventStoreOptions> _sqlServerEventStoreOptions;
	readonly IPostgresSnapshotEventStoreTelemetry _telemetry;
	readonly ISnapshotStrategy<T> _snapshotStrategy;
	readonly ISnapshotStrategySelector? _snapshotStrategySelector;

	readonly PostgresClient _sqlServerClient;

	readonly Type _aggregateType = typeof(T);
	readonly string _aggregateName;

	static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, string> AggregateTypeNames = new();

	/// <summary>
	/// Creates a new <see cref="PostgresSnapshotEventStore{T}"/>.
	/// </summary>
	/// <param name="eventStore">The non-queryable event store used to persist aggregates.</param>
	/// <param name="sqlServerEventStoreOptions">The snapshot event-store options.</param>
	/// <param name="telemetry">The telemetry sink for snapshot operations.</param>
	/// <param name="snapshotStrategy">Optional strategy controlling when snapshots are written; defaults to snapshotting always.</param>
	/// <param name="snapshotStrategySelector">Optional selector used to pick a snapshot strategy per aggregate.</param>
	/// <param name="currentTenant">Optional tenant scope applied to snapshot reads and writes.</param>
	public PostgresSnapshotEventStore(
		// Explicitly request a non-queryable event store.
		INonQueryableEventStore<T> eventStore,
		IOptions<PostgresSnapshotEventStoreOptions> sqlServerEventStoreOptions,
		IPostgresSnapshotEventStoreTelemetry telemetry,
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
	static PostgresClientOptions ResolveClientOptions(PostgresSnapshotEventStoreOptions options, string aggregateName)
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
