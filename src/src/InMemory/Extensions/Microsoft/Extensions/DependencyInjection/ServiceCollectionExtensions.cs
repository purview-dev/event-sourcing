using System.ComponentModel;
using Purview.EventSourcing.InMemory.Events;
using Purview.EventSourcing.InMemory.Snapshots;
using Purview.EventSourcing.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering the in-memory event stores with the dependency injection container.
/// </summary>
/// <remarks>
/// The members of this type are hidden from IntelliSense as the type is only intended to be consumed
/// through the <see langword="static"/> using for the <c>Microsoft.Extensions.DependencyInjection</c> namespace.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		/// Registers the <see cref="InMemoryEventStore{T}"/> implementation against the non-queryable
		/// store contracts.
		/// </summary>
		/// <returns>The same <paramref name="services"/> instance for chaining.</returns>
		/// <remarks>
		/// Registers <see cref="IEventStoreCore{T}"/>, <see cref="INonQueryableEventStore{T}"/> and
		/// <see cref="IInMemoryEventStore{T}"/> as transient services, along with the non-generic
		/// <see cref="IEventStore"/> facade. Call this once during application startup.
		/// </remarks>
		public IServiceCollection AddInMemoryEventStore()
		{
			services.AddEventSourcing();

			services
				.AddTransient(typeof(IEventStoreCore<>), typeof(InMemoryEventStore<>))
				.AddTransient(typeof(INonQueryableEventStore<>), typeof(InMemoryEventStore<>))
				.AddTransient(typeof(IInMemoryEventStore<>), typeof(InMemoryEventStore<>))
				.AddTransient<IEventStore, EventStoreFacade>();

			services.AddEventStoreCapabilities(
				new EventStoreCapabilities(
					EventStoreTransactionGuarantee.BestEffort,
					SupportsEventStreams: true,
					SupportsSnapshots: false,
					SnapshotSchemaVersioning: SnapshotSchemaSupport.None,
					PreservedMetadata: PreservedEventMetadata.All,
					SupportsQueries: false,
					SupportsIdempotencyMarkers: true,
					Concurrency: ConcurrencyGuarantee.Optimistic,
					OperationalLimitations: [EventStoreOperationalLimitation.NonPersistent]
				)
			);

			return services;
		}

		/// <summary>
		/// Registers the <see cref="InMemorySnapshotStore{T}"/> implementation against both the
		/// non-queryable and queryable store contracts.
		/// </summary>
		/// <returns>The same <paramref name="services"/> instance for chaining.</returns>
		/// <remarks>
		/// Registers <see cref="IEventStoreCore{T}"/>, <see cref="INonQueryableEventStore{T}"/>,
		/// <see cref="IInMemoryEventStore{T}"/>, <see cref="IQueryableEventStoreCore{T}"/> and
		/// <see cref="IInMemorySnapshotStore{T}"/> as transient services, along with the non-generic
		/// <see cref="IEventStore"/> and <see cref="IQueryableEventStore"/> facades. Call this once
		/// during application startup.
		/// </remarks>
		public IServiceCollection AddInMemorySnapshotEventStore()
		{
			services.AddEventSourcing();

			services
				// Non-queryable
				.AddTransient(typeof(IEventStoreCore<>), typeof(InMemorySnapshotStore<>))
				.AddTransient(typeof(INonQueryableEventStore<>), typeof(InMemorySnapshotStore<>))
				.AddTransient(typeof(IInMemoryEventStore<>), typeof(InMemorySnapshotStore<>))
				// Queryable
				.AddTransient(typeof(IQueryableEventStoreCore<>), typeof(InMemorySnapshotStore<>))
				.AddTransient(typeof(IInMemorySnapshotStore<>), typeof(InMemorySnapshotStore<>));

			services
				.AddTransient<IQueryableEventStore, QueryableEventStoreFacade>()
				.AddTransient<IEventStore, EventStoreFacade>();

			services.AddEventStoreCapabilities(
				new EventStoreCapabilities(
					EventStoreTransactionGuarantee.BestEffort,
					SupportsEventStreams: true,
					SupportsSnapshots: true,
					SnapshotSchemaVersioning: SnapshotSchemaSupport.SingleVersion,
					PreservedMetadata: PreservedEventMetadata.All,
					SupportsQueries: true,
					SupportsIdempotencyMarkers: true,
					Concurrency: ConcurrencyGuarantee.Optimistic,
					OperationalLimitations: [EventStoreOperationalLimitation.NonPersistent]
				)
			);

			return services;
		}
	}
}
