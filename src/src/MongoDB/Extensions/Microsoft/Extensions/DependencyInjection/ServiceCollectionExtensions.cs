using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Purview.EventSourcing.Internal;
using Purview.EventSourcing.MongoDB.Events;
using Purview.EventSourcing.MongoDB.Snapshots;
using Purview.EventSourcing.MongoDB.Subscriptions;
using Purview.EventSourcing.Subscriptions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering the MongoDB event stores with the dependency-injection container.
/// </summary>
/// <remarks>
/// Registers the <see cref="MongoDBEventStore{T}"/> and/or <see cref="MongoDBSnapshotEventStore{T}"/>
/// implementations along with their telemetry, options and MongoDB client infrastructure. Options are bound
/// from the <c>EventStore:MongoDB</c> and <c>EventStore:MongoDBSnapshot</c> configuration sections and
/// validated on start-up.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ServiceCollectionExtensions
{
	extension([NotNull] IServiceCollection services)
	{
		/// <summary>
		/// Registers the MongoDB-backed <see cref="MongoDBEventStore{T}"/> with the service collection.
		/// </summary>
		/// <returns>The same service collection, for chaining.</returns>
		/// <remarks>
		/// The store is registered for <see cref="IMongoDBEventStore{T}"/>, <see cref="INonQueryableEventStore{T}"/>
		/// and <see cref="IEventStoreCore{T}"/>. The connection string is resolved from the
		/// <c>EventStore_MongoDB</c> or <c>MongoDB</c> connection string when not configured explicitly.
		/// </remarks>
		public IServiceCollection AddMongoDBEventStore() => services.AddMongoDBEventStore(connectionStringName: null);

		/// <summary>
		/// Registers the MongoDB-backed <see cref="MongoDBEventStore{T}"/> with the service collection.
		/// </summary>
		/// <param name="connectionStringName">The name of the connection string to use, or <see langword="null"/> for the default.</param>
		/// <returns>The same service collection, for chaining.</returns>
		/// <remarks>
		/// The store is registered for <see cref="IMongoDBEventStore{T}"/>, <see cref="INonQueryableEventStore{T}"/>
		/// and <see cref="IEventStoreCore{T}"/>. The connection string is resolved from the
		/// named connection string, falling back to <c>EventStore_MongoDB</c> and <c>MongoDB</c>.
		/// </remarks>
		public IServiceCollection AddMongoDBEventStore(string? connectionStringName)
		{
			services.AddEventSourcing();

			services
				.AddTransient(typeof(IEventStoreCore<>), typeof(MongoDBEventStore<>))
				.AddTransient(typeof(INonQueryableEventStore<>), typeof(MongoDBEventStore<>))
				.AddTransient(typeof(IMongoDBEventStore<>), typeof(MongoDBEventStore<>))
				.AddTransient<IEventStore, EventStoreFacade>()
				.AddMongoDBEventStoreTelemetry();

			services.AddMongoDBClientTelemetry();

			services.AddEventStoreCapabilities(
				new EventStoreCapabilities(
					EventStoreTransactionGuarantee.BestEffort,
					SupportsEventStreams: true,
					SupportsSnapshots: true,
					SnapshotSchemaVersioning: SnapshotSchemaSupport.Versioned,
					PreservedMetadata: PreservedEventMetadata.All,
					SupportsQueries: false,
					SupportsIdempotencyMarkers: true,
					Concurrency: ConcurrencyGuarantee.Optimistic,
					OperationalLimitations: []
				)
			);

			services
				.AddOptions<MongoDBEventStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
					{
						configuration.GetSection(MongoDBEventStoreOptions.MongoDBEventStore).Bind(options);

						if (string.IsNullOrWhiteSpace(options.ConnectionString))
						{
							options.ConnectionString = configuration.GetRequiredConnectionString([
								connectionStringName,
								"EventStore_MongoDB",
								"MongoDB",
							]);
						}
					}
				)
				.Validate(
					static options =>
						Validator.TryValidateObject(
							options,
							new ValidationContext(options),
							validationResults: null,
							validateAllProperties: true
						),
					$"{nameof(MongoDBEventStoreOptions)} is invalid."
				)
				.ValidateOnStart();

			services.TryAddSingleton<IMongoClient>(sp =>
			{
				var options = sp.GetRequiredService<IOptions<MongoDBEventStoreOptions>>().Value;
				var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
				if (!string.IsNullOrWhiteSpace(options.ApplicationName))
					settings.ApplicationName = options.ApplicationName;

				return new MongoClient(settings);
			});

			return services;
		}

		/// <summary>
		/// Registers the MongoDB-backed queryable <see cref="MongoDBSnapshotEventStore{T}"/> with the service collection.
		/// </summary>
		/// <param name="registerAsIEventStore">When <see langword="true"/>, the store is also registered for the
		/// non-queryable <see cref="IEventStoreCore{T}"/> contract.</param>
		/// <returns>The same service collection, for chaining.</returns>
		/// <remarks>
		/// The store is registered for <see cref="IMongoDBSnapshotEventStore{T}"/> and
		/// <see cref="IQueryableEventStoreCore{T}"/>. The connection string is resolved from
		/// the <c>EventStore_MongoDBSnapshot</c> or <c>MongoDBSnapshot</c> connection string when not configured
		/// explicitly.
		/// </remarks>
		public IServiceCollection AddMongoDBSnapshotQueryableEventStore(bool registerAsIEventStore = false) =>
			services.AddMongoDBSnapshotQueryableEventStore(connectionStringName: null, registerAsIEventStore);

		/// <summary>
		/// Registers the MongoDB-backed queryable <see cref="MongoDBSnapshotEventStore{T}"/> with the service collection.
		/// </summary>
		/// <param name="connectionStringName">The name of the connection string to use, or <see langword="null"/> for the default.</param>
		/// <param name="registerAsIEventStore">When <see langword="true"/>, the store is also registered for the
		/// non-queryable <see cref="IEventStoreCore{T}"/> contract.</param>
		/// <returns>The same service collection, for chaining.</returns>
		/// <remarks>
		/// The store is registered for <see cref="IMongoDBSnapshotEventStore{T}"/> and
		/// <see cref="IQueryableEventStoreCore{T}"/>. The connection string is resolved from
		/// the named connection string, falling back to <c>EventStore_MongoDBSnapshot</c>, <c>MongoDBSnapshot</c>,
		/// <c>EventStore_MongoDB</c> and <c>MongoDB</c>.
		/// </remarks>
		public IServiceCollection AddMongoDBSnapshotQueryableEventStore(
			string? connectionStringName,
			bool registerAsIEventStore = false
		)
		{
			services.AddEventSourcing();

			services
				.AddTransient(typeof(IQueryableEventStoreCore<>), typeof(MongoDBSnapshotEventStore<>))
				.AddTransient(typeof(IMongoDBSnapshotEventStore<>), typeof(MongoDBSnapshotEventStore<>))
				.AddMongoDBSnapshotEventStoreTelemetry();
			services.AddMongoDBClientTelemetry();

			services.TryAddTransient<IQueryableEventStore, QueryableEventStoreFacade>();

			if (registerAsIEventStore)
			{
				services.AddTransient(typeof(IEventStoreCore<>), typeof(MongoDBSnapshotEventStore<>));
				services.TryAddTransient<IEventStore, EventStoreFacade>();
			}

			services.AddEventStoreCapabilities(
				new EventStoreCapabilities(
					EventStoreTransactionGuarantee.BestEffort,
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
				.AddOptions<MongoDBSnapshotEventStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
					{
						configuration.GetSection(MongoDBSnapshotEventStoreOptions.MongoDBEventStore).Bind(options);

						options.ConnectionString ??= configuration.GetRequiredConnectionString([
							connectionStringName,
							"EventStore_MongoDBSnapshot",
							"MongoDBSnapshot",
							"EventStore_MongoDB",
							"MongoDB",
						]);
					}
				)
				.Validate(
					static options =>
						Validator.TryValidateObject(
							options,
							new ValidationContext(options),
							validationResults: null,
							validateAllProperties: true
						),
					$"{nameof(MongoDBSnapshotEventStoreOptions)} is invalid."
				)
				.ValidateOnStart();

			services.TryAddSingleton<IMongoClient>(sp =>
			{
				var options = sp.GetRequiredService<IOptions<MongoDBSnapshotEventStoreOptions>>().Value;
				var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
				if (!string.IsNullOrWhiteSpace(options.ApplicationName))
					settings.ApplicationName = options.ApplicationName;

				return new MongoClient(settings);
			});

			return services;
		}

		/// <summary>
		/// Registers the MongoDB subscription checkpoint store, so subscription positions survive a
		/// restart.
		/// </summary>
		/// <returns>The <paramref name="services"/> for chaining.</returns>
		/// <remarks>
		/// Call this before <c>AddEventSubscription</c>. Without a durable checkpoint store the default
		/// in-memory one is used, and every subscription replays every stream from the beginning after a
		/// restart.
		/// </remarks>
		public IServiceCollection AddMongoDBSubscriptionCheckpoints()
		{
			services.TryAddSingleton<ISubscriptionCheckpointStore, MongoDBSubscriptionCheckpointStore>();

			services
				.AddOptions<MongoDBSubscriptionCheckpointStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
						configuration
							.GetSection(MongoDBSubscriptionCheckpointStoreOptions.MongoDBSubscriptions)
							.Bind(options)
				);

			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<MongoDBSubscriptionCheckpointStoreOptions>,
					MongoDBSubscriptionCheckpointStoreOptionsValidator
				>()
			);

			return services;
		}
	}
}
