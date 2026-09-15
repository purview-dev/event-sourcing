namespace Purview.EventSourcing.Samples.Domain;

public sealed partial class CustomerAggregateTests
{
	static CustomerAggregate CreateCustomer(string? id = null)
	{
		CustomerAggregate customer = new();
		if (id is not null)
			customer.Details.Id = id;
		return customer;
	}

	[Test]
	public async Task MultipleOperations_TracksVersionCorrectly()
	{
		// Arrange
		var customer = CreateCustomer("cust-1");

		// Act
		customer.RegisterCustomer("Jane", "jane@test.com");
		customer.ChangeEmail("new@test.com");
		customer.ChangePhoneNumber("+1-555-0100");

		// Assert — 3 events = version 3
		await Assert.That(customer.Details.CurrentVersion).IsEqualTo(3);
		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(3);
	}
}
