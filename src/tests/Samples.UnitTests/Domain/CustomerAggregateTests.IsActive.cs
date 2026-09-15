namespace Purview.EventSourcing.Samples.Domain;

partial class CustomerAggregateTests
{
	[Test]
	public async Task Deactivate_GivenActiveCustomer_SetsIsActiveFalse()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane", "jane@test.com");

		// Act
		customer.Deactivate();

		// Assert
		await Assert.That(customer.IsActive).IsFalse();
	}

	[Test]
	public async Task Deactivate_GivenAlreadyInactive_DoesNotRecordEvent()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane", "jane@test.com");
		customer.Deactivate();
		var eventCountBefore = customer.GetUnsavedEvents().Count;

		// Act
		customer.Deactivate();

		// Assert
		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(eventCountBefore);
	}

	[Test]
	public async Task Reactivate_GivenInactiveCustomer_SetsIsActiveTrue()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane", "jane@test.com");
		customer.Deactivate();

		// Act
		customer.Reactivate();

		// Assert
		await Assert.That(customer.IsActive).IsTrue();
	}

	[Test]
	public async Task Reactivate_GivenAlreadyActive_DoesNotRecordEvent()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane", "jane@test.com");
		customer.Reactivate();
		var eventCountBefore = customer.GetUnsavedEvents().Count;

		// Act
		customer.Reactivate();

		// Assert
		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(eventCountBefore);
	}
}
