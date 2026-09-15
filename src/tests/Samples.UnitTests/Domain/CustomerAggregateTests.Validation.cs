namespace Purview.EventSourcing.Samples.Domain;

partial class CustomerAggregateTests
{
	[Test]
	public async Task RegisterCustomer_GivenWhitespaceName_ThrowsArgumentException()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");

		// Act & Assert
		await Assert.That(() => customer.RegisterCustomer("   ", "valid@test.com")).Throws<ArgumentException>();
	}

	[Test]
	public async Task RegisterCustomer_GivenWhitespaceEmail_ThrowsArgumentException()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");

		// Act & Assert
		await Assert.That(() => customer.RegisterCustomer("Valid Name", "   ")).Throws<ArgumentException>();
	}

	[Test]
	public async Task ChangeEmail_GivenWhitespaceEmail_ThrowsArgumentException()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane", "old@test.com");

		// Act & Assert
		await Assert.That(() => customer.ChangeEmail("   ")).Throws<ArgumentException>();
	}

	[Test]
	public async Task ChangeName_GivenWhitespaceName_ThrowsArgumentException()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane", "jane@test.com");

		// Act & Assert
		await Assert.That(() => customer.ChangeName("   ")).Throws<ArgumentException>();
	}

	[Test]
	public async Task UpdateDetails_GivenAllNulls_RaisesNoEvents()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane Smith", "jane@test.com");
		var countBefore = customer.GetUnsavedEvents().Count;

		// Act
		customer.UpdateDetails(); // all nulls

		// Assert
		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(countBefore);
	}

	[Test]
	public async Task UpdateDetails_WithValidationError_ThrowsAndDoesNotRecordPartial()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane Smith", "jane@test.com");
		var countBefore = customer.GetUnsavedEvents().Count;

		// Act & Assert — fails on invalid name, so no events recorded
		await Assert.That(() => customer.UpdateDetails(name: "  ", email: "new@test.com")).Throws<ArgumentException>();

		// Verify state unchanged
		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(countBefore);
	}
}
