using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Fixtures.SqlServer;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.SqlServer.Events;

/// <summary>
/// Guards that the tenant query filter isolates event streams.
/// </summary>
/// <remarks>
/// Exercised against real SQL Server rather than in memory: the isolation is an Entity Framework global
/// query filter translated into SQL, so an in-memory substitute would prove nothing about the predicate
/// that actually runs.
/// </remarks>
[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerAssembly)]
public sealed class SqlServerEventStoreTenancyTests(SqlServerEventStoreFixture fixture)
{
	sealed class StubTenant(TenantScope scope) : ICurrentTenant
	{
		public TenantScope Current => scope;
	}

	SqlServerEventStore<PersistenceAggregate> StoreFor(TenantScope scope, Guid runId) =>
		fixture.CreateEventStore<PersistenceAggregate>(runId: runId, currentTenant: new StubTenant(scope));

	[Test]
	public async Task GetAsync_GivenAnotherTenantsAggregate_DoesNotReturnIt(CancellationToken cancellationToken)
	{
		// Arrange - one physical table, two tenants, the same aggregate id.
		var runId = Guid.NewGuid();
		var aggregateId = $"{Guid.NewGuid()}";

		var acme = StoreFor(TenantScope.For("acme"), runId);
		var globex = StoreFor(TenantScope.For("globex"), runId);

		var aggregate = TestHelpers.Aggregate<PersistenceAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		var saved = await acme.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await Assert.That(saved.IsSuccess).IsTrue();

		// Act
		var fromOwner = await acme.GetAsync(aggregateId, cancellationToken: cancellationToken);
		var fromOther = await globex.GetAsync(aggregateId, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(fromOwner.IsSuccess).IsTrue();
		await Assert.That(fromOther.IsFailure).IsTrue();
	}

	// The distributed cache is read before the tenant-filtered storage lookup, so an unqualified cache
	// key would let a second tenant read the first tenant's aggregate straight out of a shared cache,
	// bypassing every storage filter. Reading twice as the owner guarantees the entry is cached.
	[Test]
	public async Task GetAsync_GivenACachedAggregate_DoesNotServeItToAnotherTenant(CancellationToken cancellationToken)
	{
		var runId = Guid.NewGuid();
		var aggregateId = $"{Guid.NewGuid()}";

		var acme = StoreFor(TenantScope.For("acme"), runId);
		var globex = StoreFor(TenantScope.For("globex"), runId);

		var aggregate = TestHelpers.Aggregate<PersistenceAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();
		await acme.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Prime the cache as the owning tenant.
		await acme.GetAsync(aggregateId, cancellationToken: cancellationToken);
		await Assert.That((await acme.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsSuccess).IsTrue();

		await Assert
			.That((await globex.GetAsync(aggregateId, cancellationToken: cancellationToken)).IsFailure)
			.IsTrue();
	}

	[Test]
	public async Task GetAsync_GivenSystemScope_ReadsAcrossTenants(CancellationToken cancellationToken)
	{
		// Arrange
		var runId = Guid.NewGuid();
		var aggregateId = $"{Guid.NewGuid()}";

		var acme = StoreFor(TenantScope.For("acme"), runId);
		var system = StoreFor(TenantScope.System, runId);

		var aggregate = TestHelpers.Aggregate<PersistenceAggregate>(aggregateId: aggregateId);
		aggregate.IncrementInt32Value();

		await acme.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Act - infrastructure work deliberately crosses tenants.
		var result = await system.GetAsync(aggregateId, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();
	}
}
