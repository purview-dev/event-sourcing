using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Aggregates.Events.Upcasting;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.EventStores.NullQueryable;
using Purview.EventSourcing.Manifest;
using Purview.EventSourcing.Outbox;
using Purview.EventSourcing.Retention;
using Purview.EventSourcing.Services;
using Purview.EventSourcing.Subscriptions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Purview EventSourcing framework services and optional store implementations.
/// </summary>
/// <remarks>
/// These extension methods register the core services such as the event-name mapper, aggregate requirements
/// manager, change-feed notifier, upcaster registry, correlation ID provider, and transaction factory. They
/// are hidden from IntelliSense because they are invoked through the <see cref="IServiceCollection"/>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
[DebuggerStepThrough]
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		/// Registers the generated event-contract manifest (and an optional approved baseline) so runtime
		/// and Admin tooling can inspect the contract and its compatibility status.
		/// </summary>
		/// <param name = "formatVersion">The manifest format version (typically <c>EventContractManifest.FormatVersion</c>).</param>
		/// <param name = "json">The generated manifest JSON (typically <c>EventContractManifest.Json</c>).</param>
		/// <param name = "baselineJson">
		/// The approved baseline manifest JSON, when one is committed. When supplied, the provider
		/// reports <see cref = "EventContractCompatibilityStatus.Compatible"/> only when it matches.
		/// </param>
		/// <returns>The <paramref name = "services"/> for chaining.</returns>
		public IServiceCollection AddEventContractManifest(int formatVersion, string json, string? baselineJson = null)
		{
			ArgumentNullException.ThrowIfNull(json);
			services.TryAddSingleton<IEventContractManifestProvider>(
				new EventContractManifestProvider(formatVersion, json, baselineJson)
			);
			return services;
		}

		/// <summary>
		/// Registers an outbox store, an outbox handler, the lease-protected dispatcher, and a hosted
		/// dispatch loop.
		/// </summary>
		/// <typeparam name = "TOutboxStore">The <see cref = "IOutboxStore"/> implementation.</typeparam>
		/// <typeparam name = "TOutboxHandler">The <see cref = "IOutboxHandler"/> implementation.</typeparam>
		/// <param name = "configure">Optional <see cref = "OutboxDispatchOptions"/> configuration.</param>
		/// <returns>The <paramref name = "services"/> for chaining.</returns>
		/// <remarks>
		/// An outbox guarantees atomic persistence plus at-least-once delivery: the handler must be
		/// idempotent because a message can be delivered more than once.
		/// </remarks>
		public IServiceCollection AddOutbox<TOutboxStore, TOutboxHandler>(
			Action<OutboxDispatchOptions>? configure = null
		)
			where TOutboxStore : class, IOutboxStore
			where TOutboxHandler : class, IOutboxHandler
		{
			services.TryAddSingleton<TOutboxStore>();
			services.TryAddSingleton<IOutboxStore>(static services => services.GetRequiredService<TOutboxStore>());
			services.TryAddSingleton<TOutboxHandler>();
			services.TryAddSingleton<IOutboxHandler>(static services => services.GetRequiredService<TOutboxHandler>());
			services.TryAddSingleton<IOutboxDispatcher, OutboxDispatcher>();
			services.AddHostedService<OutboxDispatchHostedService>();
			services
				.AddOptions<OutboxDispatchOptions>()
				.Configure(options => configure?.Invoke(options))
				.Validate(
					static options =>
						options.BatchSize >= 1
						&& options.MaxAttempts >= 1
						&& options.LeaseDuration > TimeSpan.Zero
						&& options.RetryBackoffBase >= TimeSpan.Zero
						&& options.PollInterval > TimeSpan.Zero
						&& options.Retention >= TimeSpan.Zero,
					"OutboxDispatchOptions is invalid."
				);
			return services;
		}

		/// <summary>
		/// Registers crypto-shredding: a key store, the AES-GCM payload protector, and a start-up
		/// check that refuses a key store which cannot survive a restart.
		/// </summary>
		/// <typeparam name = "TKeyStore">The <see cref = "IEventCryptoKeyStore"/> implementation.</typeparam>
		/// <param name = "configure">Optional <see cref = "EventCryptoOptions"/> configuration.</param>
		/// <returns>The <paramref name = "services"/> for chaining.</returns>
		/// <remarks>
		/// <para>
		/// Every provider takes an optional <see cref = "IEventPayloadProtector"/>, so registering it here
		/// turns protection on for whichever store is configured. Events implementing
		/// <see cref = "IEventDataSubject"/> are then encrypted under that subject's key.
		/// </para>
		/// <para>
		/// Erasing a subject is only as irreversible as the least-irreversible copy of their key: a
		/// database restore or a vault's purge protection can both bring one back. See
		/// <c>docs/wiki/Crypto-Key-Management-Design.md</c>.
		/// </para>
		/// </remarks>
		public IServiceCollection AddEventCryptoShredding<TKeyStore>(Action<EventCryptoOptions>? configure = null)
			where TKeyStore : class, IEventCryptoKeyStore
		{
			services.TryAddSingleton<TKeyStore>();
			services.AddOptions<EventCryptoOptions>().Configure(options => configure?.Invoke(options));

			// The cache wraps the real store, so IEventCryptoKeyStore resolves to whichever the
			// options asked for. The start-up check resolves the same registration, so it still sees
			// through to a non-durable store underneath.
			services.TryAddSingleton<IEventCryptoKeyStore>(static provider =>
			{
				var keyStore = provider.GetRequiredService<TKeyStore>();
				var options = provider.GetRequiredService<IOptions<EventCryptoOptions>>();
				if (!options.Value.CacheKeys)
					return keyStore;

				return new CachingEventCryptoKeyStore(
					keyStore,
					options,
					provider.GetService<Purview.EventSourcing.Tenancy.ICurrentTenant>()
				);
			});

			services.TryAddSingleton<IEventPayloadProtector, AesGcmEventPayloadProtector>();
			services.AddHostedService<NonDurableKeyStoreStartupCheck>();

			return services;
		}

		/// <summary>
		/// Registers crypto-shredding with the in-memory key store.
		/// </summary>
		/// <param name = "configure">Optional <see cref = "EventCryptoOptions"/> configuration.</param>
		/// <returns>The <paramref name = "services"/> for chaining.</returns>
		/// <remarks>
		/// For tests and local development only. Keys are lost on restart, so the host refuses to start
		/// outside Development unless <see cref = "EventCryptoOptions.AllowNonDurableKeyStore"/> is set.
		/// </remarks>
		public IServiceCollection AddInMemoryEventCryptoShredding(Action<EventCryptoOptions>? configure = null) =>
			services.AddEventCryptoShredding<InMemoryEventCryptoKeyStore>(configure);

		/// <summary>
		/// Registers the event-stream retention preview for an aggregate type.
		/// </summary>
		/// <typeparam name = "TAggregate">The aggregate type to evaluate.</typeparam>
		/// <returns>The <paramref name = "services"/> for chaining.</returns>
		/// <remarks>
		/// <para>
		/// Preview only - nothing here removes an event. It reports what a policy would prune and what
		/// is holding the rest back, so a policy can be found to be wrong before it costs anything.
		/// </para>
		/// <para>
		/// It reads every stream of the aggregate type, because the snapshot version is only knowable
		/// from the aggregate itself. Run it deliberately, not on a timer.
		/// </para>
		/// <para>
		/// Subscription lag is checked against the subscriptions registered <em>in this process</em>.
		/// One running elsewhere is invisible here, so a deployment that spreads subscriptions across
		/// hosts must evaluate retention where they all are.
		/// </para>
		/// </remarks>
		public IServiceCollection AddEventStreamRetentionPreview<TAggregate>()
			where TAggregate : class, IAggregate, new()
		{
			services.TryAddScoped<IEventStreamRetentionPreview<TAggregate>, EventStreamRetentionPreview<TAggregate>>();

			// Discovery is shared with subscriptions; register the default when nothing supplied one.
			var hasDiscovery = services.Any(descriptor =>
				descriptor.ServiceType == typeof(IAggregateStreamDiscovery<TAggregate>)
				|| descriptor.ServiceType == typeof(IAggregateStreamDiscovery<>)
			);
			if (!hasDiscovery)
			{
				services.AddScoped<
					IAggregateStreamDiscovery<TAggregate>,
					EventStoreAggregateStreamDiscovery<TAggregate>
				>();
			}

			return services;
		}

		/// <summary>
		/// Registers event-stream retention: the preview, the prune, and optionally a scheduled run.
		/// </summary>
		/// <typeparam name = "TAggregate">The aggregate type to prune.</typeparam>
		/// <param name = "configure">Optional <see cref = "EventRetentionOptions"/> configuration.</param>
		/// <returns>The <paramref name = "services"/> for chaining.</returns>
		/// <remarks>
		/// <para>
		/// <strong>Nothing is destructive until <see cref = "EventRetentionOptions.Mode"/> says so.</strong>
		/// It defaults to <see cref = "EventRetentionMode.None"/>, which registers the preview and no prune -
		/// adding a registration must not start deleting history.
		/// </para>
		/// <para>
		/// Operator-invoked and scheduled are independent flags, so a deployment can enable either,
		/// both, or neither. Requires the store to implement <see cref = "IEventStreamPruning{T}"/>;
		/// a provider without it can still preview.
		/// </para>
		/// </remarks>
		public IServiceCollection AddEventStreamRetention<TAggregate>(Action<EventRetentionOptions>? configure = null)
			where TAggregate : class, IAggregate, new()
		{
			services.AddEventStreamRetentionPreview<TAggregate>();
			services.TryAddScoped<IEventStreamRetention<TAggregate>, EventStreamRetention<TAggregate>>();

			services
				.AddOptions<EventRetentionOptions>()
				.Configure(options => configure?.Invoke(options))
				.Validate(
					static options =>
					{
						options.Validate();

						return true;
					},
					"EventRetentionOptions is invalid."
				);

			services.AddHostedService<EventRetentionHostedService<TAggregate>>();

			return services;
		}

		/// <summary>
		/// Registers a subscriber, its catch-up processor, and a hosted loop that drives it.
		/// </summary>
		/// <typeparam name = "TAggregate">The aggregate type to subscribe to.</typeparam>
		/// <typeparam name = "TSubscriber">The <see cref = "IEventSubscriber{T}"/> implementation.</typeparam>
		/// <param name = "configure">Optional <see cref = "SubscriptionOptions"/> configuration.</param>
		/// <returns>The <paramref name = "services"/> for chaining.</returns>
		/// <remarks>
		/// <para>
		/// Unlike the change feed and the outbox, a subscription reads the event stream itself, so it
		/// delivers events written before the subscriber existed. Delivery is at-least-once and ordered
		/// within an aggregate, so the subscriber must be idempotent.
		/// </para>
		/// <para>
		/// Registers <see cref = "InMemorySubscriptionCheckpointStore"/> only if no
		/// <see cref = "ISubscriptionCheckpointStore"/> is already registered. That default loses every
		/// position on restart, which replays all streams; register a durable store in a deployed
		/// application.
		/// </para>
		/// <para>
		/// Stream discovery defaults to <see cref = "EventStoreAggregateStreamDiscovery{T}"/>, which works on
		/// every provider that persists an event stream. Register an
		/// <see cref = "IAggregateStreamDiscovery{T}"/> before calling this to narrow it - for example to the
		/// aggregates changed since a watermark, rather than all of them.
		/// </para>
		/// </remarks>
		public IServiceCollection AddEventSubscription<TAggregate, TSubscriber>(
			Action<SubscriptionOptions>? configure = null
		)
			where TAggregate : class, IAggregate, new()
			where TSubscriber : class, IEventSubscriber<TAggregate>
		{
			services.TryAddScoped<IEventSubscriber<TAggregate>, TSubscriber>();
			services.TryAddSingleton<ISubscriptionCheckpointStore, InMemorySubscriptionCheckpointStore>();
			// TryAdd compares service types exactly, so it would not see an open-generic registration
			// supplied by a provider or a consumer; check both shapes before adding the default.
			var hasDiscovery = services.Any(descriptor =>
				descriptor.ServiceType == typeof(IAggregateStreamDiscovery<TAggregate>)
				|| descriptor.ServiceType == typeof(IAggregateStreamDiscovery<>)
			);
			if (!hasDiscovery)
			{
				services.AddScoped<
					IAggregateStreamDiscovery<TAggregate>,
					EventStoreAggregateStreamDiscovery<TAggregate>
				>();
			}
			// A null object, not an absent registration: the processor always has a sink to call, and
			// this one accepts nothing, so the default behaviour is unchanged - a failing event stalls
			// its stream rather than being passed over. An application opts in by registering its own.
			services.TryAddSingleton<ISubscriptionDeadLetterSink, NoSubscriptionDeadLetterSink>();
			// Singleton, because the processor is scoped and rebuilt every cycle; a counter inside it
			// would reset each time and never reach any threshold.
			services.TryAddSingleton<ISubscriptionDeliveryAttemptTracker, InMemorySubscriptionDeliveryAttemptTracker>();
			services.TryAddScoped<IEventSubscriptionProcessor<TAggregate>, EventSubscriptionProcessor<TAggregate>>();
			services.AddHostedService<SubscriptionHostedService<TAggregate>>();
			services
				.AddOptions<SubscriptionOptions>()
				.Configure(options => configure?.Invoke(options))
				.Validate(
					static options =>
						options.PollInterval > TimeSpan.Zero
						&& options.CheckpointInterval >= 1
						&& options.MaxDeliveryAttempts >= 0,
					"SubscriptionOptions is invalid."
				);
			return services;
		}

		/// <summary>
		/// Register the event store components.
		/// </summary>
		/// <returns>The <paramref name="services"/> passed in.</returns>
		public IServiceCollection AddEventSourcing()
		{
			services
				.AddSingleton<IAggregateEventNameMapper, AggregateEventNameMapper>()
				.AddAggregateChangeFeedNotifierTelemetry()
				.AddScoped<IAggregateRequirementsManager, AggregateRequiredServiceManager>()
				.AddScoped(typeof(IAggregateChangeFeedNotifier<>), typeof(AggregateChangeFeedNotifier<>))
				.AddSingleton<IEventUpcasterRegistry, EventUpcasterRegistry>();

			services.TryAddSingleton<IEventStoreCorrelationIdProvider, ActivityEventStoreCorrelationIdProvider>();

			// TryAdd, so a host that knows who the current user is - a web application resolving it from
			// the request - replaces the ambient default by registering its own first.
			services.TryAddSingleton<IEventStorePrincipalAccessor, AmbientEventStorePrincipalAccessor>();

			// Defaults to TenantScope.System, which applies no tenant filter, so a single-tenant
			// deployment behaves as before. A multi-tenant host registers its own resolver.
			services.TryAddSingleton<
				Purview.EventSourcing.Tenancy.ICurrentTenant,
				Purview.EventSourcing.Tenancy.AmbientCurrentTenant
			>();

			// The generated recorder needs an IMeterFactory, which only a host that configured metrics
			// provides. Falling back keeps a host that has not - a console application, a test - working
			// instead of failing to resolve the event store; recording metrics is never a precondition
			// for persisting events.
			services.TryAddSingleton<IEventStoreMetrics>(static serviceProvider =>
				serviceProvider.GetService<System.Diagnostics.Metrics.IMeterFactory>() is null
					? NoOpEventStoreMetrics.Instance
					: ActivatorUtilities.CreateInstance<EventStoreMetricsCore>(serviceProvider)
			);
			services.TryAddSingleton<IEventStoreTransactionFactory, EventStoreTransactionFactory>();
			services.TryAddSingleton<IAggregateTypeRegistry, AssemblyAggregateTypeRegistry>();

			// Capability discovery is always available and reports the conservative default until a
			// provider registers its truthful capabilities.
			services.TryAddSingleton<IEventStoreCapabilitiesProvider, EventStoreCapabilitiesProvider>();

			return services;
		}

		/// <summary>
		/// Registers the event-store capabilities that a provider actually implements. Custom providers
		/// must not claim stronger behavior than they provide; <see cref="EventStoreCapabilities.Default"/>
		/// is the conservative baseline used when nothing is registered.
		/// </summary>
		/// <param name="capabilities">The truthful capabilities of the registered store.</param>
		/// <returns>The <paramref name="services"/> passed in.</returns>
		public IServiceCollection AddEventStoreCapabilities(EventStoreCapabilities capabilities)
		{
			ArgumentNullException.ThrowIfNull(capabilities);

			services.TryAddSingleton<IEventStoreCapabilitiesProvider, EventStoreCapabilitiesProvider>();
			services.AddSingleton(capabilities);

			return services;
		}

		/// <summary>
		/// Registers a null (non-persisting) queryable event store and its facade, useful for development and
		/// testing scenarios.
		/// </summary>
		/// <returns>The <paramref name="services"/> passed in.</returns>
		public IServiceCollection AddNullQueryableEventStore()
		{
			services
				.AddTransient(typeof(IQueryableEventStoreCore<>), typeof(NullQueryableEventStore<>))
				.TryAddTransient<IQueryableEventStore, QueryableEventStoreFacade>();

			return services;
		}

		/// <summary>
		/// Registers an <see cref="IEventUpcaster{TSource,TTarget}"/> so that legacy events stored
		/// under <typeparamref name="TSource"/> are automatically up-cast to
		/// <typeparamref name="TTarget"/> during event replay.
		/// </summary>
		/// <typeparam name="TSource">The legacy event type.</typeparam>
		/// <typeparam name="TTarget">The current event type.</typeparam>
		/// <typeparam name="TUpcaster">The upcaster implementation.</typeparam>
		/// <returns>The <paramref name="services"/> for fluent chaining.</returns>
		/// <remarks>
		/// This method also registers a corresponding <see cref="IEventUpcasterDescriptor"/> so that
		/// the <see cref="IEventUpcasterRegistry"/> can discover the upcaster at runtime.
		/// <see cref="AddEventSourcing"/> must be called before (or after) this method on the same
		/// <paramref name="services"/> collection.
		/// </remarks>
		public IServiceCollection AddEventUpcaster<TSource, TTarget, TUpcaster>()
			where TSource : class
			where TTarget : class
			where TUpcaster : class, IEventUpcaster<TSource, TTarget>
		{
			services
				.AddSingleton<IEventUpcaster<TSource, TTarget>, TUpcaster>()
				.AddSingleton<IEventUpcasterDescriptor>(sp => new EventUpcasterDescriptor<TSource, TTarget>(
					sp.GetRequiredService<IEventUpcaster<TSource, TTarget>>()
				));

			return services;
		}
	}
}
