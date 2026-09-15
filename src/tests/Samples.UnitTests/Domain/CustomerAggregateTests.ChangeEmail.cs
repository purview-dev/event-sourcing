namespace Purview.EventSourcing.Samples.Domain;

partial class CustomerAggregateTests
{
	[Test]
	public async Task ChangeEmail_GivenNewEmail_UpdatesEmail()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane", "old@test.com");

		// Act
		customer.ChangeEmail("new@test.com");

		// Assert
		await Assert.That(customer.Email).IsEqualTo("new@test.com");
	}

	[Test]
	public async Task ChangeEmail_GivenSameEmail_DoesNotRecordEvent()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane", "same@test.com");
		var eventCountBefore = customer.GetUnsavedEvents().Count;

		// Act
		customer.ChangeEmail("same@test.com");

		// Assert — no new event recorded
		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(eventCountBefore);
	}

	[Test]
	public void ChangeEmail_GivenEmptyEmail_ThrowsArgumentException()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane", "old@test.com");

		// Act & Assert
		Assert.Throws<ArgumentException>(() => customer.ChangeEmail("  "));
	}
}
