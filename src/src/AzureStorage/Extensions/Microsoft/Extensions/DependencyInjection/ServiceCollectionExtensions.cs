using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.AzureStorage.Cryptography;
using Purview.EventSourcing.AzureStorage.Subscriptions;
using Purview.EventSourcing.Cryptography;
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

		/// <summary>
		/// Registers crypto-shredding backed by a durable Azure Table Storage key store.
		/// </summary>
		/// <param name="configure">Optional <see cref="EventCryptoOptions"/> configuration.</param>
		/// <returns>The <paramref name="services"/> for chaining.</returns>
		/// <remarks>
		/// Keys are scoped to the current tenant and survive a restart, so this is what makes
		/// crypto-shredding usable in a deployed application. Note that restoring the key store from a
		/// backup brings shredded keys back; see <c>docs/wiki/Crypto-Key-Management-Design.md</c>.
		/// </remarks>
		public IServiceCollection AddAzureStorageEventCryptoShredding(Action<EventCryptoOptions>? configure = null)
		{
			services.AddEventCryptoShredding<AzureStorageEventCryptoKeyStore>(configure);

			services
				.AddOptions<AzureStorageEventCryptoKeyStoreOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
						configuration
							.GetSection(AzureStorageEventCryptoKeyStoreOptions.AzureStorageCryptography)
							.Bind(options)
				);

			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<
					IValidateOptions<AzureStorageEventCryptoKeyStoreOptions>,
					AzureStorageEventCryptoKeyStoreOptionsValidator
				>()
			);

			return services;
		}
	}
}
