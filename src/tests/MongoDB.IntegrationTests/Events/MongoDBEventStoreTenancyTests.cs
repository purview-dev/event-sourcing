using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Fixtures.MongoDB;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.MongoDB.Events;

/// <summary>
/// Guards that the tenant query filter isolates event streams.
/// </summary>
/// <remarks>
/// Exercised against real MongoDB rather than in memory: the isolation is an Entity Framework global
/// query filter translated into SQL, so an in-memory substitute would prove nothing about the predicate
/// that actually runs.
/// </remarks>
[ClassDataSource<MongoDBEventStoreFixture>(Shared = SharedType.PerAssembly)]
public sealed class MongoDBEventStoreTenancyTests(MongoDBEventStoreFixture fixture)
{
	sealed class StubTenant(TenantScope scope) : ICurrentTenant
	{
		public TenantScope Current => scope;
	}

	MongoDBEventStore<PersistenceAggregate> StoreFor(TenantScope scope, Guid runId) =>
		fixture.CreateEventStore<PersistenceAggregate>(currentTenant: new StubTenant(scope), runId: runId);

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
