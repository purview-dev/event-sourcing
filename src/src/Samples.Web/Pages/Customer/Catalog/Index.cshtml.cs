using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Services;
using Purview.EventSourcing.Samples.Web.Services;

namespace Purview.EventSourcing.Samples.Web.Pages.Customer.Catalog;

sealed record CatalogProductViewModel(
	string ProductId,
	string ProductName,
	int TotalAvailable,
	string BestInventoryId,
	decimal UnitPrice,
	string? ImageUrl
);

sealed class IndexModel(
	IQueryableEventStore customerStore,
	IQueryableEventStore inventoryStore,
	IProductImageService imageService
) : PageModel
{
	[BindProperty(SupportsGet = true)]
	public string? Search { get; set; }

	public CustomerAggregate? CurrentCustomer { get; private set; }
	public IReadOnlyList<CatalogProductViewModel> Products { get; private set; } = [];
	public int CartCount { get; private set; }

	public async Task<IActionResult> OnGetAsync()
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		var ct = HttpContext.RequestAborted;
		CurrentCustomer = await customerStore.GetAsync<CustomerAggregate>(customerId, ct);

		ContinuationRequest request = new() { MaxRecords = 500 };
		var inventoryResult = await inventoryStore.ListAsync<InventoryAggregate>(
			q => q.OrderBy(i => i.ProductName),
			request,
			ct
		);
		var allItems = inventoryResult.Results;

		var search = Search ?? string.Empty;
		var grouped = allItems
			.Where(i =>
				string.IsNullOrEmpty(search)
				|| EF.Functions.Contains(i.ProductName, search)
				|| EF.Functions.Contains(i.ProductId, search)
			)
			.GroupBy(i => i.ProductId)
			.Where(g => g.Sum(i => i.AvailableQuantity) > 0)
			.Select(g =>
			{
				var best = g.OrderByDescending(i => i.AvailableQuantity).First();
				var totalAvailable = g.Sum(i => i.AvailableQuantity);
				var unitPrice = Math.Round(
					9.99m + (Math.Abs(g.Key.GetHashCode(StringComparison.Ordinal)) % 9000 / 100m),
					2
				);
				return (
					ProductId: g.Key,
					best.ProductName,
					TotalAvailable: totalAvailable,
					BestInventoryId: best.Id(),
					UnitPrice: unitPrice
				);
			})
			.OrderBy(p => p.ProductName)
			.ToList();

		// Resolve image URLs in parallel
		var imageUrlTasks = grouped.Select(p => imageService.GetImageUrlAsync(p.ProductId, ct)).ToList();
		var imageUrls = await Task.WhenAll(imageUrlTasks);

		Products =
		[
			.. grouped.Zip(
				imageUrls,
				(p, url) =>
					new CatalogProductViewModel(
						p.ProductId,
						p.ProductName,
						p.TotalAvailable,
						p.BestInventoryId,
						p.UnitPrice,
						url
					)
			),
		];

		CartCount = HttpContext.Session.GetCartCount();
		return Page();
	}

	public IActionResult OnPostAddToCart(
		string inventoryId,
		string productId,
		string productName,
		decimal unitPrice,
		int quantity = 1
	)
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		if (quantity < 1)
			quantity = 1;

		var cart = HttpContext.Session.GetCart();
		var idx = cart.FindIndex(c => c.InventoryId == inventoryId);
		if (idx >= 0)
			cart[idx] = cart[idx] with { Quantity = cart[idx].Quantity + quantity };
		else
			cart.Add(new CartItem(productId, productName, inventoryId, quantity, unitPrice));

		HttpContext.Session.SetCart(cart);
		TempData["Success"] = $"'{productName}' added to cart.";
		return RedirectToPage();
	}
}
