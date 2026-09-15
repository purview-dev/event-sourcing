namespace Purview.EventSourcing.Samples.Domain;

partial class CustomerAggregateTests
{
	[Test]
	public async Task RegisterCustomer_GivenValidData_SetsPropertiesCorrectly()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");

		// Act
		customer.RegisterCustomer("Jane Smith", "jane@test.com");

		// Assert
		await Assert.That(customer.Name).IsEqualTo("Jane Smith");
		await Assert.That(customer.Email).IsEqualTo("jane@test.com");
		await Assert.That(customer.IsActive).IsTrue();
	}

	[Test]
	public async Task RegisterCustomer_GivenValidData_RecordsOneEvent()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");

		// Act
		customer.RegisterCustomer("Jane Smith", "jane@test.com");

		// Assert
		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(1);
		await Assert.That(customer.GetUnsavedEvents()[0].Event).IsTypeOf<CustomerEvents.CustomerRegisteredEvent>();
	}

	[Test]
	public void RegisterCustomer_GivenNullName_ThrowsArgumentException()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");

		// Act & Assert
		Assert.Throws<ArgumentException>(() => customer.RegisterCustomer(null!, "email@test.com"));
	}

	[Test]
	public void RegisterCustomer_GivenEmptyEmail_ThrowsArgumentException()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");

		// Act & Assert
		Assert.Throws<ArgumentException>(() => customer.RegisterCustomer("Name", ""));
	}
}
