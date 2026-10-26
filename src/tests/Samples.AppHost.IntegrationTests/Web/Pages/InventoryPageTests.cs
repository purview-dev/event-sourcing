using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Fixtures;

namespace Purview.EventSourcing.Samples.Web.Pages;

[NotInParallel("SamplesAppHost")]
[ClassDataSource<AppHostFixture>(Shared = SharedType.PerTestSession)]
public sealed class InventoryPageTests(AppHostFixture fixture)
{
	readonly HttpClient _client = fixture.CreateWebClient();

	[Test]
	public async Task BackOfficeCatalogIndex_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Catalog", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task BackOfficeCatalogCreate_Get_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Catalog/Create", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task BackOfficeCatalogCreate_Post_RedirectsToIndex(CancellationToken cancellationToken)
	{
		var antiForgery = await GetAntiForgeryTokenAsync("/BackOffice/Catalog/Create", cancellationToken);
		var pageResponse = await _client.GetAsync("/BackOffice/Catalog/Create", cancellationToken);
		var html = await pageResponse.Content.ReadAsStringAsync(cancellationToken);
		var locationId = ExtractFirstSelectValue(html, "LocationId");

		if (string.IsNullOrEmpty(locationId))
		{
			await Assert.That(pageResponse.IsSuccessStatusCode).IsTrue();
			return;
		}

		Dictionary<string, string> form = new()
		{
			["ProductId"] = "SKU-TEST-001",
			["ProductName"] = "Test Widget",
			["LocationId"] = locationId,
			["InitialQuantity"] = "50",
			["__RequestVerificationToken"] = antiForgery,
		};

		using FormUrlEncodedContent content = new(form);
		var response = await _client.PostAsync("/BackOffice/Catalog/Create", content, cancellationToken);

		await Assert.That((int)response.StatusCode).IsEqualTo(302);
		await Assert.That(response.Headers.Location?.ToString()).Contains("/BackOffice/Catalog");
	}

	[Test]
	public async Task BackOfficeCatalogDeleted_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Catalog/Deleted", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task BackOfficeStockIndex_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Stock", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task BackOfficeStockCreate_Get_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Stock/Create", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task BackOfficeStockDeleted_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Stock/Deleted", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task BackOfficeCatalogIndex_WithPaging_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Catalog?page=1&pageSize=10", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task BackOfficeCatalogIndex_WithMultiplePages_RendersNextPageLink(CancellationToken cancellationToken)
	{
		var prefix = $"paging-catalog-{Guid.NewGuid():N}";
		await CreateInventoryItemsAsync(prefix, 11, cancellationToken);

		var response = await _client.GetAsync($"/BackOffice/Catalog?search={prefix}&pageSize=10", cancellationToken);
		var html = await response.Content.ReadAsStringAsync(cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
		await Assert.That(html).Contains("page=2");
	}

	[Test]
	public async Task BackOfficeCatalogIndex_WithSorting_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Catalog?sortBy=productid&sortDir=desc", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task BackOfficeCatalogIndex_WithSearch_Returns200(CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync("/BackOffice/Catalog?search=widget", cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
	}

	[Test]
	public async Task BackOfficeStockIndex_WithMultiplePages_RendersNextPageLink(CancellationToken cancellationToken)
	{
		var prefix = $"paging-stock-{Guid.NewGuid():N}";
		await CreateInventoryItemsAsync(prefix, 11, cancellationToken);

		var response = await _client.GetAsync($"/BackOffice/Stock?search={prefix}&pageSize=10", cancellationToken);
		var html = await response.Content.ReadAsStringAsync(cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
		await Assert.That(html).Contains("page=2");
	}

	async Task<string> GetAntiForgeryTokenAsync(string url, CancellationToken cancellationToken)
	{
		var response = await _client.GetAsync(url, cancellationToken);
		var html = await response.Content.ReadAsStringAsync(cancellationToken);

		var start = html.IndexOf("__RequestVerificationToken", StringComparison.Ordinal);
		if (start == -1)
			return string.Empty;

		var valueStart = html.IndexOf("value=\"", start, StringComparison.Ordinal) + 7;
		var valueEnd = html.IndexOf('"', valueStart, StringComparison.Ordinal);

		return html[valueStart..valueEnd];
	}

	static string ExtractFirstSelectValue(string html, string selectName)
	{
		var marker = $"name=\"{selectName}\"";
		var selectStart = html.IndexOf(marker, StringComparison.Ordinal);
		if (selectStart == -1)
			return string.Empty;

		var searchFrom = selectStart;
		while (true)
		{
			var optionStart = html.IndexOf("<option value=\"", searchFrom, StringComparison.Ordinal);
			if (optionStart == -1)
				return string.Empty;

			var valueStart = optionStart + 15;
			var valueEnd = html.IndexOf('"', valueStart, StringComparison.Ordinal);
			if (valueEnd == -1)
				return string.Empty;

			var value = html[valueStart..valueEnd];
			if (!string.IsNullOrEmpty(value))
				return value;

			searchFrom = valueEnd + 1;
		}
	}

	async Task CreateInventoryItemsAsync(string prefix, int count, CancellationToken cancellationToken)
	{
		var store = fixture.QueryableEventStore();

		var locationId = $"LOC-{prefix}";
		var location = await store.CreateAsync<LocationAggregate>(locationId, cancellationToken);
		location.Create(locationId, $"Location {prefix}");
		await store.SaveAsync(location, cancellationToken);

		for (var i = 0; i < count; i++)
		{
			var item = await store.CreateAsync<InventoryAggregate>(cancellationToken: cancellationToken);
			item.Create(
				$"{prefix}-{i:D2}",
				$"{prefix}-item-{i:D2}",
				locationId,
				location.LocationName,
				initialQuantity: 10 + i
			);
			await store.SaveAsync(item, cancellationToken);
		}
	}
}
