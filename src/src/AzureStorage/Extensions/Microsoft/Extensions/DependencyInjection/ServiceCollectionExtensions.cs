using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.AzureStorage.Subscriptions;
using Purview.EventSourcing.Internal;
using Purview.EventSourcing.Subscriptions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Azure Table and Blob Storage event store with the dependency-injection container.
/// </summary>
/// <remarks>
/// Registers <see cref="ITableEventStore{T}"/>, the underlying typed stores, the
/// <see cref="EventStoreFacade"/>, and the event store options. The options are bound from the
/// <see cref="AzureStorageEventStoreOptions.AzureStorageEventStore"/> configuration section and validated on start-up.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		/// Registers the Azure Table and Blob Storage event store, optionally specifying the configuration
		/// connection-string name to use when the options do not contain a connection string.
		/// </summary>
		/// <param name="connectionStringName">Optional, the name of the configuration connection string to use.</param>
		/// <returns>The <see cref="IServiceCollection"/> instance the event store was registered with, for chaining.</returns>
		public IServiceCollection AddAzureStorageEventStore(string? connectionStringName = null)
		{
			services.AddEventSourcing();

			services
				.AddTransient(typeof(IEventStoreCore<>), typeof(TableEventStore<>))
				.AddTransient(typeof(INonQueryableEventStore<>), typeof(TableEventStore<>))
				.AddTransient(typeof(ITableEventStore<>), typeof(TableEventStore<>))
				.AddTransient<IEventStore, EventStoreFacade>()
				.AddTableEventStoreTelemetry();

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
				.AddOptions<AzureStorageEventStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
					{
						configuration.GetSection(AzureStorageEventStoreOptions.AzureStorageEventStore).Bind(options);

						if (string.IsNullOrWhiteSpace(options.ConnectionString))
						{
							options.ConnectionString = configuration.GetRequiredConnectionString([
								connectionStringName,
								"EventStore_AzureStorage",
								"AzureStorage",
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
					$"{nameof(AzureStorageEventStoreOptions)} is invalid."
				)
				.ValidateOnStart();

			return services;
		}

		/// <summary>
		/// Registers the Azure Table Storage subscription checkpoint store, so subscription positions
		/// survive a restart.
		/// </summary>
		/// <returns>The <paramref name="services"/> for chaining.</returns>
		/// <remarks>
		/// Call this before <c>AddEventSubscription</c>. Without a durable checkpoint store the default
		/// in-memory one is used, and every subscription replays every stream from the beginning after a
		/// restart.
		/// </remarks>
		public IServiceCollection AddAzureStorageSubscriptionCheckpoints()
		{
			services.TryAddSingleton<ISubscriptionCheckpointStore, AzureStorageSubscriptionCheckpointStore>();

			services
				.AddOptions<AzureStorageSubscriptionCheckpointStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
						configuration
							.GetSection(AzureStorageSubscriptionCheckpointStoreOptions.AzureStorageSubscriptions)
							.Bind(options)
				);

			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<AzureStorageSubscriptionCheckpointStoreOptions>,
					AzureStorageSubscriptionCheckpointStoreOptionsValidator
				>()
			);

			return services;
		}
	}
}
