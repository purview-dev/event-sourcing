using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Aggregates.Events.Upcasting;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.EventStores.NullQueryable;
using Purview.EventSourcing.Manifest;
using Purview.EventSourcing.Outbox;
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
		/// Stream discovery defaults to <see cref = "QueryableAggregateStreamDiscovery{T}"/>, which needs a
		/// queryable snapshot store. Register an <see cref = "IAggregateStreamDiscovery{T}"/> before calling
		/// this to use a provider-specific implementation instead.
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
			// TryAdd compares service types exactly, so a closed-generic TryAdd would not see a
			// provider's open-generic registration and would win by being registered last.
			var hasDiscovery = services.Any(descriptor =>
				descriptor.ServiceType == typeof(IAggregateStreamDiscovery<TAggregate>)
				|| descriptor.ServiceType == typeof(IAggregateStreamDiscovery<>)
			);
			if (!hasDiscovery)
			{
				services.AddScoped<
					IAggregateStreamDiscovery<TAggregate>,
					QueryableAggregateStreamDiscovery<TAggregate>
				>();
			}
			services.TryAddScoped<IEventSubscriptionProcessor<TAggregate>, EventSubscriptionProcessor<TAggregate>>();
			services.AddHostedService<SubscriptionHostedService<TAggregate>>();
			services
				.AddOptions<SubscriptionOptions>()
				.Configure(options => configure?.Invoke(options))
				.Validate(
					static options => options.PollInterval > TimeSpan.Zero && options.CheckpointInterval >= 1,
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
