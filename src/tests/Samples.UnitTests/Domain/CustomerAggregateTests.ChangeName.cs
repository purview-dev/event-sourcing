namespace Purview.EventSourcing.Samples.Domain;

partial class CustomerAggregateTests
{
	[Test]
	public async Task ChangeName_GivenNewName_UpdatesName()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane Smith", "jane@test.com");

		// Act
		customer.ChangeName("Jane Doe");

		// Assert
		await Assert.That(customer.Name).IsEqualTo("Jane Doe");
	}

	[Test]
	public async Task ChangeName_GivenNewName_RecordsEvent()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane Smith", "jane@test.com");
		var countBefore = customer.GetUnsavedEvents().Count;

		// Act
		customer.ChangeName("Jane Doe");

		// Assert
		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(countBefore + 1);
	}

	[Test]
	public async Task ChangeName_GivenSameName_DoesNotRecordEvent()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane Smith", "jane@test.com");
		var countBefore = customer.GetUnsavedEvents().Count;

		// Act
		customer.ChangeName("Jane Smith");

		// Assert — no new event recorded
		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(countBefore);
	}

	[Test]
	public void ChangeName_GivenEmptyName_ThrowsArgumentException()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane Smith", "jane@test.com");

		// Act & Assert
		Assert.Throws<ArgumentException>(() => customer.ChangeName("  "));
	}
}
