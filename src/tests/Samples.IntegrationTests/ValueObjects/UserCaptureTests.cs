using Purview.EventSourcing.Fixtures.SqlServer;

namespace Purview.EventSourcing.Samples.ValueObjects;

[ClassDataSource<SqlServerSnapshotEventStoreFixture>(Shared = SharedType.PerTestSession)]
public class UserCaptureTests(SqlServerSnapshotEventStoreFixture fixture)
{
	static readonly Faker Faker = new();

	[Test]
	public async Task UserCapture_GivenComplexValueObject_DoesNotThrowOnSnapshot(CancellationToken cancellationToken)
	{
		// Arrange
		var eventStore = fixture.CreateSnapshotStore<ValueObjectTestAggregate>();
		var aggregate = CreateTestAggregate();

		// Act
		Task Act() => eventStore.SnapshotAsync(aggregate, cancellationToken);

		// Assert
		await Assert.That(Act).ThrowsNothing();
	}

	[Test]
	public async Task UserCapture_GivenComplexValueObject_CanStoreAndRetrieveInEF(CancellationToken cancellationToken)
	{
		// Arrange
		var eventStore = fixture.CreateSnapshotStore<ValueObjectTestAggregate>();
		var aggregate = CreateTestAggregate();

		// Act
		await eventStore.SnapshotAsync(aggregate, cancellationToken);
		var retrievedAggregateResult = await eventStore.GetAsync(aggregate.Id(), cancellationToken);
		var retrievedAggregate = retrievedAggregateResult.IsSuccess ? retrievedAggregateResult.Value : null;

		// Assert
		await Assert.That(retrievedAggregate).IsNotNull();
		await Assert.That(retrievedAggregate!.UserCapture).IsEqualTo(aggregate.UserCapture);
	}

	static UserCapture CreateSUT(UserDetails? user = null, DateTimeOffset? occurredAt = null) =>
		UserCapture.Create(user ?? CreateUserDetails(), occurredAt ?? Faker.Date.RecentOffset());

	static UserDetails CreateUserDetails(Guid? id = null, string? displayName = null, bool isActive = true) =>
		new(id ?? Faker.Random.Guid(), displayName ?? Faker.Person.FullName, isActive);

	static ValueObjectTestAggregate CreateTestAggregate(UserCapture? user = null)
	{
		ValueObjectTestAggregate aggregate = new();
		aggregate.Details.Id = Faker.Random.Guid().ToString();

		aggregate.SetUserCapture(user ?? CreateSUT());

		return aggregate;
	}
}
