using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Fixtures.SqlServer;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.ValueObjects;

namespace Purview.EventSourcing.SqlServer.Snapshots;

[ClassDataSource<SqlServerSnapshotEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class SqlServerSnapshotQueryTranslationBoundaryTests(SqlServerSnapshotEventStoreFixture fixture)
{
	[Test]
	public async Task QueryAsync_GivenPrimitiveScalarValueObjectMembers_Translates(CancellationToken cancellationToken)
	{
		var store = fixture.CreateSnapshotStore<CustomerAggregate>();
		CustomerAggregate customer = new() { Details = { Id = Guid.NewGuid().ToString("D") } };
		customer.RegisterCustomer("Jane Smith", "jane@test.com");
		customer.ChangeEmail("updated@test.com");

		await store.SnapshotAsync(customer, cancellationToken);

		var query = await store.QueryAsync(
			m => m.Email == "updated@test.com" && m.Name == "Jane Smith",
			cancellationToken: cancellationToken
		);

		await Assert.That(query.Results).Count().IsEqualTo(1);
		await Assert.That(query.Results[0].Id()).IsEqualTo(customer.Id());
		await Assert.That(query.Results[0].Email).IsEqualTo("updated@test.com");
		await Assert.That(query.Results[0].Name).IsEqualTo("Jane Smith");
	}

	[Test]
	public async Task QueryAsync_GivenNonScalarValueObjectMembers_Translates(CancellationToken cancellationToken)
	{
		var store = fixture.CreateSnapshotStore<SnapshotValueObjectsAggregate>();
		SnapshotValueObjectsAggregate aggregate = new();
		aggregate.Details.Id = Guid.NewGuid().ToString("D");
		aggregate.CaptureUserDetails(
			UserDetails.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Jane Snapshot", true),
			UserDetails2.Create(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Jane Snapshot 2")
		);

		await store.SaveAsync(aggregate, operationContext: null, cancellationToken);

		var query = await store.QueryAsync(
			m => m.UserDetails.DisplayName == "Jane Snapshot" && m.UserDetails2.DisplayName == "Jane Snapshot 2",
			cancellationToken: cancellationToken
		);

		await Assert.That(query.Results).Count().IsEqualTo(1);
		await Assert.That(query.Results[0].Id()).IsEqualTo(aggregate.Id());
		await Assert.That(query.Results[0].UserDetails.DisplayName).IsEqualTo("Jane Snapshot");
		await Assert.That(query.Results[0].UserDetails2.DisplayName).IsEqualTo("Jane Snapshot 2");
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506:Avoid excessive class coupling",
		Justification = "Test wiring: the store, its infrastructure and the result model are all named here."
	)]
	public async Task QueryAsync_GivenDirectlyMappedComplexNestedMembers_Translates(CancellationToken cancellationToken)
	{
		var store = fixture.CreateSnapshotStore<PersistenceAggregate>();
		PersistenceAggregate aggregate = new() { Details = { Id = Guid.NewGuid().ToString("D") } };
		aggregate.SetComplexProperty(
			new Aggregates.ComplexTestType
			{
				Int16Property = 16,
				Int32Property = 32,
				Int64Property = 64,
				StringProperty = "complex-test",
				DateTimeOffsetProperty = DateTimeOffset.UtcNow,
			}
		);

		await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

		var query = await store.QueryAsync(
			m =>
				m.ComplexTestType != null
				&& m.ComplexTestType.StringProperty == "complex-test"
				&& m.ComplexTestType.Int32Property == 32,
			cancellationToken: cancellationToken
		);

		await Assert.That(query.Results).Count().IsEqualTo(1);
		await Assert.That(query.Results[0].Id()).IsEqualTo(aggregate.Id());
		await Assert.That(query.Results[0].ComplexTestType).IsNotNull();
		await Assert.That(query.Results[0].ComplexTestType!.StringProperty).IsEqualTo("complex-test");
		await Assert.That(query.Results[0].ComplexTestType!.Int32Property).IsEqualTo(32);
	}
}
