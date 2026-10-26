using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.InMemory.Events;
using Purview.EventSourcing.Subscriptions;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing;

/// <summary>
/// The state one subscription scenario builds up and asserts against.
/// </summary>
/// <remarks>
/// Cycles are run explicitly rather than by the hosted service, so a scenario is deterministic: it
/// states when the subscription reads, and asserts what it read.
/// </remarks>
public sealed class SubscriptionScenarioContext : IDisposable
{
	readonly ServiceProvider _provider;

	/// <summary>Creates the context, its store, and its subscription.</summary>
	public SubscriptionScenarioContext()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddEventSourcing().AddInMemoryEventStore();

		// The in-memory store keeps its data in the instance, and is registered transient, so the
		// subscription would otherwise read a different, empty store than the scenario writes to.
		services.AddSingleton<IInMemoryEventStore<AcceptanceAggregate>, InMemoryEventStore<AcceptanceAggregate>>();
		services.AddSingleton<IEventStoreCore<AcceptanceAggregate>>(static provider =>
			provider.GetRequiredService<IInMemoryEventStore<AcceptanceAggregate>>()
		);

		// Registered before AddEventSubscription so its TryAdd leaves this shared instance in place.
		Subscriber = new RecordingSubscriber();
		services.AddSingleton<IEventSubscriber<AcceptanceAggregate>>(Subscriber);

		services.AddEventSubscription<AcceptanceAggregate, RecordingSubscriber>();

		_provider = services.BuildServiceProvider();
		Store = _provider.GetRequiredService<IInMemoryEventStore<AcceptanceAggregate>>();
	}

	/// <summary>The store every step writes through.</summary>
	public IInMemoryEventStore<AcceptanceAggregate> Store { get; }

	/// <summary>The subscriber whose deliveries the scenario asserts on.</summary>
	public RecordingSubscriber Subscriber { get; }

	/// <summary>The aggregate identifier the scenario is working with.</summary>
	public string AggregateId { get; set; } = $"{Guid.NewGuid()}";

	/// <summary>The result of the most recent catch-up cycle.</summary>
	public SubscriptionCycleResult? LastCycle { get; set; }

	/// <summary>Runs one catch-up cycle as the given tenant.</summary>
	/// <param name="tenant">The tenant, or <see langword="null"/> for the system scope.</param>
	public async Task<SubscriptionCycleResult> RunCycleAsync(string? tenant)
	{
		var scope = tenant is null ? TenantScope.System : TenantScope.For(tenant);

		using (AmbientCurrentTenant.BeginScope(scope))
		{
			using var serviceScope = _provider.CreateScope();
			var processor = serviceScope.ServiceProvider.GetRequiredService<
				IEventSubscriptionProcessor<AcceptanceAggregate>
			>();

			var result = await processor.RunCycleAsync(CancellationToken.None);
			LastCycle = result;

			return result;
		}
	}

	/// <summary>Runs an action under a tenant scope.</summary>
	/// <param name="tenant">The tenant, or <see langword="null"/> for the system scope.</param>
	/// <param name="action">The work to run.</param>
	public static async Task AsTenantAsync(string? tenant, Func<Task> action)
	{
		var scope = tenant is null ? TenantScope.System : TenantScope.For(tenant);

		using (AmbientCurrentTenant.BeginScope(scope))
			await action();
	}

	/// <summary>The operation context scenarios use; no principal is configured.</summary>
	public static EventStoreOperationContext OperationContext() => new() { RequiresValidPrincipalIdentifier = false };

	/// <inheritdoc/>
	public void Dispose() => _provider.Dispose();
}
