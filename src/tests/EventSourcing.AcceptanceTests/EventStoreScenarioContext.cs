using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.InMemory.Events;
using Purview.EventSourcing.Tenancy;
using Purview.Results;

namespace Purview.EventSourcing;

/// <summary>
/// The state one acceptance scenario builds up and asserts against.
/// </summary>
/// <remarks>
/// Backed by the in-memory store so a scenario describing a guarantee runs anywhere, with no container.
/// The in-memory store enforces the same tenant isolation and returns the same result cases as the
/// persistent providers, which is what these scenarios are about; behaviour that depends on real storage
/// - query translation, crypto-shredding, transactions - belongs in the provider integration suites.
/// </remarks>
public sealed class EventStoreScenarioContext : IDisposable
{
	readonly ServiceProvider _provider;

	/// <summary>Creates the context and its store.</summary>
	public EventStoreScenarioContext()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddEventSourcing().AddInMemoryEventStore();

		_provider = services.BuildServiceProvider();
		Store = _provider.GetRequiredService<IInMemoryEventStore<AcceptanceAggregate>>();
	}

	/// <summary>The store every step acts through.</summary>
	public IInMemoryEventStore<AcceptanceAggregate> Store { get; }

	/// <summary>The aggregate identifier the scenario is working with.</summary>
	public string AggregateId { get; set; } = $"{Guid.NewGuid()}";

	/// <summary>The aggregate most recently created or read.</summary>
	public AcceptanceAggregate? Aggregate { get; set; }

	/// <summary>The result of the most recent read.</summary>
	public Result<AcceptanceAggregate, AggregateReadFailure>? ReadResult { get; set; }

	/// <summary>The result of the most recent save.</summary>
	public Result<SaveOutcome<AcceptanceAggregate>, AggregateSaveFailure>? SaveResult { get; set; }

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
