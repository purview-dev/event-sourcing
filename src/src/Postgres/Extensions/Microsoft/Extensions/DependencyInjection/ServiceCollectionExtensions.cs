using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.Internal;
using Purview.EventSourcing.Outbox;
using Purview.EventSourcing.Postgres.Cryptography;
using Purview.EventSourcing.Postgres.Events;
using Purview.EventSourcing.Postgres.Outbox;
using Purview.EventSourcing.Postgres.Snapshot;
using Purview.EventSourcing.Postgres.Snapshots;
using Purview.EventSourcing.Postgres.Subscriptions;
using Purview.EventSourcing.Subscriptions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering the PostgreSQL event stores with the dependency-injection container.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ServiceCollectionExtensions
{
	static readonly string[] EventsConnectionStringNames =
	[
		"eventstore-events-postgres",
		"EventStore_Events_Postgres",
		"Events_Postgres",
		"eventstore-events-sql",
		"EventStore_Events_Sql",
		"Events_Sql",
	];

	static readonly string[] SnapshotsConnectionStringNames =
	[
		"eventstore-snapshots-postgres",
		"EventStore_Snapshots_Postgres",
		"Snapshots_Postgres",
		"eventstore-snapshots-sql",
		"EventStore_Snapshots_Sql",
		"Snapshots_Sql",
	];

	static readonly string[] DefaultConnectionStringNames =
	[
		"eventstore-postgres",
		"EventStore_Postgres",
		"Postgres",
		"eventstore-sql",
		"EventStore_Sql",
		"Sql",
	];

	extension(IServiceCollection services)
	{
		/// <summary>
		/// Registers the PostgreSQL event store and its dependencies.
		/// </summary>
		/// <param name="connectionStringName">
		/// Optional name of the connection string to use; when null, the first of the well-known
		/// event-store connection string names is used, falling back to the default connection string.
		/// </param>
		/// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
		public IServiceCollection AddPostgresEventStore(string? connectionStringName = null)
		{
			services.AddEventSourcing();
			services.AddNpgsqlDataSource(connectionStringName ?? EventsConnectionStringNames[0]);
			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<PostgresEventStoreOptions>,
					PostgresEventStoreOptionsValidator
				>()
			);

			services
				.AddTransient(typeof(IEventStoreCore<>), typeof(PostgresEventStore<>))
				.AddTransient(typeof(INonQueryableEventStore<>), typeof(PostgresEventStore<>))
				.AddTransient(typeof(IPostgresEventStore<>), typeof(PostgresEventStore<>))
				.AddTransient<IEventStore, EventStoreFacade>();
			services.TryAddSingleton<IPostgresEventStoreTransactionFactory, PostgresEventStoreTransactionFactory>();

			services.AddPostgresEventStoreTelemetry();

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
				.AddOptions<PostgresEventStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
					{
						configuration.GetSection(PostgresEventStoreOptions.PostgresEventStore).Bind(options);
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
		/// Registers the PostgreSQL queryable snapshot event store and its dependencies.
		/// </summary>
		/// <param name="connectionStringName">
		/// Optional name of the connection string to use; when null, the first of the well-known
		/// snapshot connection string names is used, falling back to the default connection string.
		/// </param>
		/// <param name="registerAsIEventStore">
		/// When <see langword="true"/>, the snapshot store is also registered as an <see cref="IEventStore"/>,
		/// allowing it to be resolved for event-store operations.
		/// </param>
		/// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
		public IServiceCollection AddPostgresSnapshotQueryableEventStore(
			string? connectionStringName = null,
			bool registerAsIEventStore = false
		)
		{
			services.AddEventSourcing();
			services.AddNpgsqlDataSource(connectionStringName ?? SnapshotsConnectionStringNames[0]);
			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<PostgresSnapshotEventStoreOptions>,
					PostgresSnapshotEventStoreOptionsValidator
				>()
			);

			services
				.AddTransient(typeof(IQueryableEventStoreCore<>), typeof(PostgresSnapshotEventStore<>))
				.AddTransient(typeof(IPostgresSnapshotEventStore<>), typeof(PostgresSnapshotEventStore<>))
				.AddPostgresSnapshotEventStoreTelemetry();

			services.TryAddTransient<IQueryableEventStore, QueryableEventStoreFacade>();

			if (registerAsIEventStore)
			{
				services.AddTransient(typeof(IEventStoreCore<>), typeof(PostgresSnapshotEventStore<>));
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
				.AddOptions<PostgresSnapshotEventStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
					{
						configuration.GetSection(PostgresSnapshotEventStoreOptions.PostgresEventStore).Bind(options);
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
		/// Registers the PostgreSQL transactional outbox: an outbox store that persists messages
		/// atomically with event saves, a lease-protected dispatcher, and a hosted dispatch loop.
		/// </summary>
		/// <typeparam name="TOutboxHandler">The <see cref="IOutboxHandler"/> implementation.</typeparam>
		/// <param name="configure">Optional outbox dispatch configuration.</param>
		/// <returns>The <paramref name="services"/> for chaining.</returns>
		/// <remarks>
		/// An outbox guarantees atomic persistence plus at-least-once delivery; the handler must be
		/// idempotent. Enqueue messages inside the event transaction with
		/// <c>PostgresOutboxStore.EnqueueInTransactionAsync</c> (typically via
		/// <see cref="IPostgresEventStoreTransaction.Enlist(Func{Npgsql.NpgsqlConnection, Npgsql.NpgsqlTransaction, CancellationToken, Task})"/>).
		/// </remarks>
		public IServiceCollection AddPostgresOutbox<TOutboxHandler>(Action<OutboxDispatchOptions>? configure = null)
			where TOutboxHandler : class, IOutboxHandler
		{
			services.AddEventSourcing();
			services.AddOutbox<PostgresOutboxStore, TOutboxHandler>(configure);

			services
				.AddOptions<PostgresOutboxStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
						configuration.GetSection(PostgresOutboxStoreOptions.PostgresOutbox).Bind(options)
				);

			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<PostgresOutboxStoreOptions>,
					PostgresOutboxStoreOptionsValidator
				>()
			);

			return services;
		}

		/// <summary>
		/// Registers the PostgreSQL subscription checkpoint store, so subscription positions survive a
		/// restart.
		/// </summary>
		/// <returns>The <paramref name="services"/> for chaining.</returns>
		/// <remarks>
		/// Call this before <c>AddEventSubscription</c>. Without a durable checkpoint store the default
		/// in-memory one is used, and every subscription replays every stream from the beginning after a
		/// restart.
		/// </remarks>
		public IServiceCollection AddPostgresSubscriptionCheckpoints()
		{
			services.TryAddSingleton<ISubscriptionCheckpointStore, PostgresSubscriptionCheckpointStore>();

			services
				.AddOptions<PostgresSubscriptionCheckpointStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
						configuration
							.GetSection(PostgresSubscriptionCheckpointStoreOptions.PostgresSubscriptions)
							.Bind(options)
				);

			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<PostgresSubscriptionCheckpointStoreOptions>,
					PostgresSubscriptionCheckpointStoreOptionsValidator
				>()
			);

			return services;
		}

		/// <summary>
		/// Registers crypto-shredding backed by a durable PostgreSQL key table.
		/// </summary>
		/// <param name="configure">Optional <see cref="EventCryptoOptions"/> configuration.</param>
		/// <returns>The <paramref name="services"/> for chaining.</returns>
		/// <remarks>
		/// Keys are scoped to the current tenant and survive a restart, so this is what makes
		/// crypto-shredding usable in a deployed application. Note that a point-in-time restore of the
		/// key table brings shredded keys back; see <c>docs/wiki/Crypto-Key-Management-Design.md</c>.
		/// </remarks>
		public IServiceCollection AddPostgresEventCryptoShredding(Action<EventCryptoOptions>? configure = null)
		{
			services.AddEventCryptoShredding<PostgresEventCryptoKeyStore>(configure);

			services
				.AddOptions<PostgresEventCryptoKeyStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
						configuration.GetSection(PostgresEventCryptoKeyStoreOptions.PostgresCryptography).Bind(options)
				);

			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<PostgresEventCryptoKeyStoreOptions>,
					PostgresEventCryptoKeyStoreOptionsValidator
				>()
			);

			return services;
		}
	}
}
