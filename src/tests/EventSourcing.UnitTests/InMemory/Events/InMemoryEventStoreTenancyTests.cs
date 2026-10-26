using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.InMemory.Events;

/// <summary>
/// Guards that the in-memory store isolates tenants.
/// </summary>
/// <remarks>
/// One store instance throughout, with the ambient scope switched between calls - two stores would prove
/// nothing but that two dictionaries hold different data. Ownership is tracked beside the data rather than
/// folded into the key, so the system scope still reads across tenants, matching the SQL providers this
/// store stands in for.
/// </remarks>
public sealed class InMemoryEventStoreTenancyTests
{
	[EventContract]
	sealed record ValueIncremented
	{
		public static int SchemaVersion => 1;

		public EventMetadata Metadata { get; init; }

		public int Amount { get; set; }
	}

	sealed class TestAggregate : AggregateBase
	{
		public int Value { get; private set; }

		protected override void RegisterEvents() => Register<ValueIncremented>(ev => Value += ev.Amount);

		public void Increment(int amount) => RecordAndApply(new ValueIncremented { Amount = amount });
	}

	static ServiceProvider CreateProvider()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddEventSourcing().AddInMemoryEventStore();
		return services.BuildServiceProvider();
	}

	static EventStoreOperationContext Context() => new() { RequiresValidPrincipalIdentifier = false };

	static async Task<IInMemoryEventStore<TestAggregate>> SeedAsync(ServiceProvider provider, string aggregateId)
	{
		var store = provider.GetRequiredService<IInMemoryEventStore<TestAggregate>>();

		using (AmbientCurrentTenant.BeginScope(TenantScope.For("acme")))
		{
			var aggregate = await store.CreateAsync(aggregateId);
			aggregate.Increment(1);
			await store.SaveAsync(aggregate, Context());
		}

		return store;
	}

	[Test]
	public async Task GetAsync_GivenAnotherTenantsAggregate_DoesNotReturnIt()
	{
		using var provider = CreateProvider();
		var store = await SeedAsync(provider, "tenancy-1");

		using (AmbientCurrentTenant.BeginScope(TenantScope.For("acme")))
			await Assert.That((await store.GetAsync("tenancy-1", Context())).IsSuccess).IsTrue();

		using (AmbientCurrentTenant.BeginScope(TenantScope.For("globex")))
			await Assert.That((await store.GetAsync("tenancy-1", Context())).IsFailure).IsTrue();
	}

	[Test]
	public async Task ExistsAsync_GivenAnotherTenantsAggregate_ReportsItAbsent()
	{
		using var provider = CreateProvider();
		var store = await SeedAsync(provider, "tenancy-2");

		using (AmbientCurrentTenant.BeginScope(TenantScope.For("globex")))
		{
			var existence = await store.ExistsAsync("tenancy-2");

			await Assert.That(existence.DoesExist).IsFalse();
		}
	}

	[Test]
	public async Task GetEventRangeAsync_GivenAnotherTenantsAggregate_ReturnsNothing()
	{
		using var provider = CreateProvider();
		var store = await SeedAsync(provider, "tenancy-3");

		using (AmbientCurrentTenant.BeginScope(TenantScope.For("globex")))
		{
			List<(EventRecord EventRecord, string EventType)> events = [];
			await foreach (
				var @event in ((IEventStoreCore<TestAggregate>)store).GetEventRangeAsync(
					"tenancy-3",
					1,
					null,
					CancellationToken.None
				)
			)
				events.Add(@event);

			await Assert.That(events).IsEmpty();
		}
	}

	// Data written with no tenant - a tenant-less deployment, or a write path that missed the stamp -
	// must not become visible to every tenant. Failing closed turns a missed stamp into absent data
	// rather than silent cross-tenant exposure.
	[Test]
	public async Task GetAsync_GivenAnUnownedStream_IsNotVisibleToATenant()
	{
		using var provider = CreateProvider();
		var store = provider.GetRequiredService<IInMemoryEventStore<TestAggregate>>();

		// Seeded under the system scope, so the stream has no owner.
		using (AmbientCurrentTenant.BeginScope(TenantScope.System))
		{
			var aggregate = await store.CreateAsync("unowned-1");
			aggregate.Increment(1);
			await store.SaveAsync(aggregate, Context());
		}

		using (AmbientCurrentTenant.BeginScope(TenantScope.For("acme")))
			await Assert.That((await store.GetAsync("unowned-1", Context())).IsFailure).IsTrue();

		using (AmbientCurrentTenant.BeginScope(TenantScope.System))
			await Assert.That((await store.GetAsync("unowned-1", Context())).IsSuccess).IsTrue();
	}

	[Test]
	public async Task GetAsync_GivenTheSystemScope_ReadsAcrossTenants()
	{
		using var provider = CreateProvider();
		var store = await SeedAsync(provider, "tenancy-4");

		using (AmbientCurrentTenant.BeginScope(TenantScope.System))
			await Assert.That((await store.GetAsync("tenancy-4", Context())).IsSuccess).IsTrue();
	}
}
