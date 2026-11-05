using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Fixtures;

namespace Purview.EventSourcing.Samples.Web.Pages;

[NotInParallel("SamplesAppHost")]
[ClassDataSource<AppHostFixture>(Shared = SharedType.PerTestSession)]
public sealed class OrderPageTests(AppHostFixture fixture)
{
	readonly HttpClient _client = fixture.CreateWebClient();

	[Test]
	public async Task BackOfficeIndex_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task CustomerOrders_WithoutSession_RedirectsOrReturns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/Customer/Orders", cancellationToken);

		await Assert.That((int)response.StatusCode is 200 or 302).IsTrue();
	}

	[Test]
	public async Task CustomerOrders_WithMultiplePages_RendersNextPageLink(CancellationToken cancellationToken)
	{
		var customerId = await CreateCustomerWithOrdersAsync(11, cancellationToken);
		var antiForgery = await GetAntiForgeryTokenAsync(_client, "/Customer", cancellationToken);

		using FormUrlEncodedContent content = new([
			new("id", customerId),
			new("__RequestVerificationToken", antiForgery),
		]);
		var selectResponse = await _client.PostAsync("/Customer?handler=Select", content, cancellationToken);
		await Assert.That((int)selectResponse.StatusCode).IsEqualTo(302);

		var response = await _client.GetAsync("/Customer/Orders?pageSize=10", cancellationToken);
		var html = await response.Content.ReadAsStringAsync(cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
		await Assert.That(html).Contains("page=2");
	}

	[Test]
	public async Task BackOfficeStockTransfer_Get_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Stock/Transfer", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task BackOfficeStockTransfer_Post_WithValidData(CancellationToken cancellationToken)
	{
		using var client = fixture.CreateWebClient(followRedirects: true);
		var (sourceInventoryId, destinationLocationId) = await CreateTransferScenarioAsync(cancellationToken);
		var antiForgery = await GetAntiForgeryTokenAsync(client, "/BackOffice/Stock/Transfer", cancellationToken);

		Dictionary<string, string> form = new()
		{
			["SourceInventoryId"] = sourceInventoryId,
			["DestinationLocationId"] = destinationLocationId,
			["Quantity"] = "1",
			["Reason"] = "Integration test transfer",
			["__RequestVerificationToken"] = antiForgery,
		};

		using FormUrlEncodedContent content = new(form);
		var response = await client.PostAsync("/BackOffice/Stock/Transfer", content, cancellationToken);

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
	}

	[Test]
	public async Task BackOfficeCatalogIndex_WithPaging_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Catalog?page=1&pageSize=10", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task BackOfficeCatalogIndex_WithSorting_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Catalog?sortBy=name&sortDir=asc", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task BackOfficeCatalogDeleted_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Catalog/Deleted", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	async Task<(string SourceInventoryId, string DestinationLocationId)> CreateTransferScenarioAsync(
		CancellationToken cancellationToken
	)
	{
		var store = fixture.QueryableEventStore();

		var sourceLocationId = $"LOC-TEST-SRC-{Guid.NewGuid():N}";
		var sourceLocation = await store.CreateAsync<LocationAggregate>(sourceLocationId, cancellationToken);
		sourceLocation.Create(sourceLocationId, $"Transfer Source {Guid.NewGuid():N}");
		await store.SaveAsync(sourceLocation, cancellationToken);

		var destinationLocationId = $"LOC-TEST-DST-{Guid.NewGuid():N}";
		var destinationLocation = await store.CreateAsync<LocationAggregate>(destinationLocationId, cancellationToken);
		destinationLocation.Create(destinationLocationId, $"Transfer Destination {Guid.NewGuid():N}");
		await store.SaveAsync(destinationLocation, cancellationToken);

		var inventory = await store.CreateAsync<InventoryAggregate>(cancellationToken: cancellationToken);
		inventory.Create(
			$"SKU-TRANSFER-{Guid.NewGuid():N}",
			"Transfer Test Widget",
			sourceLocationId,
			sourceLocation.LocationName,
			initialQuantity: 10
		);

		var saveResult = await store.SaveAsync(inventory, cancellationToken);
		return (saveResult.Value.Aggregate.Id(), destinationLocationId);
	}

	async Task<string> GetAntiForgeryTokenAsync(HttpClient client, string url, CancellationToken cancellationToken)
	{
		var response = await client.GetAsync(url, cancellationToken);
		var html = await response.Content.ReadAsStringAsync(cancellationToken);

		var start = html.IndexOf("__RequestVerificationToken", StringComparison.Ordinal);
		if (start == -1)
			return string.Empty;

		var valueStart = html.IndexOf("value=\"", start, StringComparison.Ordinal) + 7;
		var valueEnd = html.IndexOf('"', valueStart, StringComparison.Ordinal);

		return html[valueStart..valueEnd];
	}

	async Task<string> CreateCustomerWithOrdersAsync(int orderCount, CancellationToken cancellationToken)
	{
		var store = fixture.QueryableEventStore();
		var customer = await store.CreateAsync<CustomerAggregate>(cancellationToken: cancellationToken);
		customer.RegisterCustomer($"paging-orders-{Guid.NewGuid():N}", $"paging-orders-{Guid.NewGuid():N}@example.com");
		var customerResult = await store.SaveAsync(customer, cancellationToken);
		var customerId = customerResult.Value.Aggregate.Id();

		for (var i = 0; i < orderCount; i++)
		{
			var order = await store.CreateAsync<OrderAggregate>(cancellationToken: cancellationToken);
			order
				.CreateOrder(customerId)
				.AddLineItem($"SKU-PAGING-{i:D2}", $"Paging Item {i:D2}", 1, 9.99m + i)
				.SetShippingAddress($"{i + 1} Pagination Road");

			await store.SaveAsync(order, cancellationToken);
		}

		return customerId;
	}
}
