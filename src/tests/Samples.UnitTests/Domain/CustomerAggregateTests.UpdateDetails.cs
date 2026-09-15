namespace Purview.EventSourcing.Samples.Domain;

partial class CustomerAggregateTests
{
	[Test]
	public async Task UpdateDetails_GivenNameAndEmail_RaisesTwoEvents()
	{
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane Smith", "jane@test.com");
		var countBefore = customer.GetUnsavedEvents().Count;

		customer.UpdateDetails(name: "Jane Doe", email: "janedoe@test.com");

		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(countBefore + 2);
		await Assert.That(customer.Name).IsEqualTo("Jane Doe");
		await Assert.That(customer.Email).IsEqualTo("janedoe@test.com");
	}

	[Test]
	public async Task UpdateDetails_GivenAllThreeFields_RaisesThreeEvents()
	{
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane Smith", "jane@test.com");
		var countBefore = customer.GetUnsavedEvents().Count;

		customer.UpdateDetails(name: "Jane Doe", email: "janedoe@test.com", phoneNumber: "+44 7700 900123");

		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(countBefore + 3);
		await Assert.That(customer.Name).IsEqualTo("Jane Doe");
		await Assert.That(customer.Email).IsEqualTo("janedoe@test.com");
		await Assert.That(customer.PhoneNumber).IsEqualTo("+44 7700 900123");
	}

	[Test]
	public async Task UpdateDetails_GivenOnlyName_RaisesOneEvent()
	{
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane Smith", "jane@test.com");
		var countBefore = customer.GetUnsavedEvents().Count;

		customer.UpdateDetails(name: "Jane Doe");

		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(countBefore + 1);
		await Assert.That(customer.Name).IsEqualTo("Jane Doe");
		await Assert.That(customer.Email).IsEqualTo("jane@test.com");
	}

	[Test]
	public async Task UpdateDetails_GivenSameValues_RaisesNoEvents()
	{
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane Smith", "jane@test.com");
		var countBefore = customer.GetUnsavedEvents().Count;

		customer.UpdateDetails(name: "Jane Smith", email: "jane@test.com");

		await Assert.That(customer.GetUnsavedEvents().Count).IsEqualTo(countBefore);
	}

	[Test]
	public void UpdateDetails_GivenWhitespaceName_ThrowsArgumentException()
	{
		var customer = CreateCustomer("cust-1");
		customer.RegisterCustomer("Jane Smith", "jane@test.com");

		Assert.Throws<ArgumentException>(() => customer.UpdateDetails(name: "  "));
	}
}
