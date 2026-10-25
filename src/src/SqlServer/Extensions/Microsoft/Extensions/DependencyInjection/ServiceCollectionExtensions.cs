using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Internal;
using Purview.EventSourcing.Outbox;
using Purview.EventSourcing.SqlServer.Events;
using Purview.EventSourcing.SqlServer.Outbox;
using Purview.EventSourcing.SqlServer.Snapshot;
using Purview.EventSourcing.SqlServer.Snapshots;
using Purview.EventSourcing.SqlServer.Subscriptions;
using Purview.EventSourcing.Subscriptions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dependency-injection extension methods for registering the SQL Server event-store and snapshot stores.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ServiceCollectionExtensions
{
	static readonly string[] EventsConnectionStringNames =
	[
		"eventstore-events-sqlserver",
		"EventStore_Events_SqlServer",
		"Events_SqlServer",
		"eventstore-events-sql",
		"EventStore_Events_Sql",
		"Events_Sql",
	];

	static readonly string[] SnapshotsConnectionStringNames =
	[
		"eventstore-snapshots-sqlserver",
		"EventStore_Snapshots_SqlServer",
		"Snapshots_SqlServer",
		"eventstore-snapshots-sql",
		"EventStore_Snapshots_Sql",
		"Snapshots_Sql",
	];

	static readonly string[] DefaultConnectionStringNames =
	[
		"eventstore-sqlserver",
		"EventStore_SqlServer",
		"SqlServer",
		"eventstore-sql",
		"EventStore_Sql",
		"Sql",
	];

	extension(IServiceCollection services)
	{
		/// <summary>
		/// Registers the SQL Server event store and its supporting services.
		/// </summary>
		/// <param name="connectionStringName">
		/// Optional name of the connection string to use. When omitted, the well-known connection-string names are
		/// consulted in order.
		/// </param>
		/// <returns>The same service collection, for chaining.</returns>
		/// <remarks>
		/// Registers <see cref="SqlServerEventStore{T}"/> as the event-store implementation, configures
		/// <see cref="SqlServerEventStoreOptions"/> from the <c>EventStore:SqlServer</c> section, and enables
		/// validation on startup.
		/// </remarks>
		public IServiceCollection AddSqlServerEventStore(string? connectionStringName = null)
		{
			services.AddEventSourcing();
			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<SqlServerEventStoreOptions>,
					SqlServerEventStoreOptionsValidator
				>()
			);

			services
				.AddTransient(typeof(IEventStoreCore<>), typeof(SqlServerEventStore<>))
				.AddTransient(typeof(INonQueryableEventStore<>), typeof(SqlServerEventStore<>))
				.AddTransient(typeof(ISqlServerEventStore<>), typeof(SqlServerEventStore<>))
				.AddTransient<IEventStore, EventStoreFacade>();
			services.TryAddSingleton<ISqlServerEventStoreTransactionFactory, SqlServerEventStoreTransactionFactory>();

			services.AddSqlServerEventStoreTelemetry();

			services.AddEventStoreCapabilities(
				new EventStoreCapabilities(
					EventStoreTransactionGuarantee.Atomic,
					SupportsEventStreams: true,
					SupportsSnapshots: true,
					SnapshotSchemaVersioning: SnapshotSchemaSupport.Versioned,
					PreservedMetadata: PreservedEventMetadata.All,
					SupportsQueries: false,
					SupportsIdempotencyMarkers: true,
					Concurrency: ConcurrencyGuarantee.Optimistic,
					OperationalLimitations: []
				)
				{
					SupportsTransactionalOutbox = true,
				}
			);

			services
				.AddOptions<SqlServerEventStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
					{
						configuration.GetSection(SqlServerEventStoreOptions.SqlServerEventStore).Bind(options);
						if (string.IsNullOrWhiteSpace(options.ConnectionString))
						{
							options.ConnectionString = configuration.GetRequiredConnectionString([
								connectionStringName,
								.. EventsConnectionStringNames,
								.. DefaultConnectionStringNames,
							]);
						}
					}
				)
				.ValidateOnStart();

			return services;
		}

		/// <summary>
		/// Registers the SQL Server snapshot (queryable) event store and its supporting services.
		/// </summary>
		/// <param name="connectionStringName">
		/// Optional name of the connection string to use. When omitted, the well-known connection-string names are
		/// consulted in order.
		/// </param>
		/// <param name="registerAsIEventStore">
		/// When <see langword="true"/>, the snapshot store is also registered as the non-queryable
		/// <c>IEventStore</c> implementation.
		/// </param>
		/// <returns>The same service collection, for chaining.</returns>
		/// <remarks>
		/// Registers <see cref="SqlServerSnapshotEventStore{T}"/> as the queryable event-store implementation,
		/// configures <see cref="SqlServerSnapshotEventStoreOptions"/> from the <c>EventStore:SqlServerSnapshot</c>
		/// section, and enables validation on startup.
		/// </remarks>
		public IServiceCollection AddSqlServerSnapshotQueryableEventStore(
			string? connectionStringName = null,
			bool registerAsIEventStore = false
		)
		{
			services.AddEventSourcing();
			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<SqlServerSnapshotEventStoreOptions>,
					SqlServerSnapshotEventStoreOptionsValidator
				>()
			);

			services
				.AddTransient(typeof(IQueryableEventStoreCore<>), typeof(SqlServerSnapshotEventStore<>))
				.AddTransient(typeof(ISqlServerSnapshotEventStore<>), typeof(SqlServerSnapshotEventStore<>))
				.AddSqlServerSnapshotEventStoreTelemetry();

			services.TryAddTransient<IQueryableEventStore, QueryableEventStoreFacade>();

			if (registerAsIEventStore)
			{
				services.AddTransient(typeof(IEventStoreCore<>), typeof(SqlServerSnapshotEventStore<>));
				services.TryAddTransient<IEventStore, EventStoreFacade>();
			}

			services.AddEventStoreCapabilities(
				new EventStoreCapabilities(
					EventStoreTransactionGuarantee.Atomic,
					SupportsEventStreams: false,
					SupportsSnapshots: true,
					SnapshotSchemaVersioning: SnapshotSchemaSupport.SingleVersion,
					PreservedMetadata: PreservedEventMetadata.None,
					SupportsQueries: true,
					SupportsIdempotencyMarkers: false,
					Concurrency: ConcurrencyGuarantee.Optimistic,
					OperationalLimitations: []
				)
			);

			services
				.AddOptions<SqlServerSnapshotEventStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
					{
						configuration.GetSection(SqlServerSnapshotEventStoreOptions.SqlServerEventStore).Bind(options);
						if (string.IsNullOrWhiteSpace(options.ConnectionString))
						{
							options.ConnectionString = configuration.GetRequiredConnectionString([
								connectionStringName,
								.. SnapshotsConnectionStringNames,
								.. DefaultConnectionStringNames,
							]);
						}
					}
				)
				.ValidateOnStart();

			return services;
		}

		/// <summary>
		/// Registers the SQL Server transactional outbox: an outbox store that persists messages
		/// atomically with event saves, a lease-protected dispatcher, and a hosted dispatch loop.
		/// </summary>
		/// <typeparam name="TOutboxHandler">The <see cref="IOutboxHandler"/> implementation.</typeparam>
		/// <param name="configure">Optional outbox dispatch configuration.</param>
		/// <returns>The <paramref name="services"/> for chaining.</returns>
		/// <remarks>
		/// An outbox guarantees atomic persistence plus at-least-once delivery; the handler must be
		/// idempotent. Enqueue messages inside the event transaction with
		/// <c>SqlServerOutboxStore.EnqueueInTransactionAsync</c> (typically via
		/// <see cref="ISqlServerEventStoreTransaction.Enlist(Func{Data.SqlClient.SqlConnection, Data.SqlClient.SqlTransaction, CancellationToken, Task})"/>).
		/// </remarks>
		public IServiceCollection AddSqlServerOutbox<TOutboxHandler>(Action<OutboxDispatchOptions>? configure = null)
			where TOutboxHandler : class, IOutboxHandler
		{
			services.AddEventSourcing();
			services.AddOutbox<SqlServerOutboxStore, TOutboxHandler>(configure);

			services
				.AddOptions<SqlServerOutboxStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
						configuration.GetSection(SqlServerOutboxStoreOptions.SqlServerOutbox).Bind(options)
				);

			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<SqlServerOutboxStoreOptions>,
					SqlServerOutboxStoreOptionsValidator
				>()
			);

			return services;
		}

		/// <summary>
		/// Registers the SQL Server subscription checkpoint store, so subscription positions survive a
		/// restart.
		/// </summary>
		/// <returns>The <paramref name="services"/> for chaining.</returns>
		/// <remarks>
		/// Call this before <c>AddEventSubscription</c>. Without a durable checkpoint store the default
		/// in-memory one is used, and every subscription replays every stream from the beginning after a
		/// restart.
		/// </remarks>
		public IServiceCollection AddSqlServerSubscriptionCheckpoints()
		{
			services.TryAddSingleton<ISubscriptionCheckpointStore, SqlServerSubscriptionCheckpointStore>();

			services
				.AddOptions<SqlServerSubscriptionCheckpointStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
						configuration
							.GetSection(SqlServerSubscriptionCheckpointStoreOptions.SqlServerSubscriptions)
							.Bind(options)
				);

			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<SqlServerSubscriptionCheckpointStoreOptions>,
					SqlServerSubscriptionCheckpointStoreOptionsValidator
				>()
			);

			return services;
		}
	}
}
