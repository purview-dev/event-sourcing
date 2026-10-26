using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Services;

namespace Purview.EventSourcing.Samples.Web.Pages.Customer.Catalog;

sealed class OrderModel(
	IOrderFulfilmentService fulfilmentService,
	IQueryableEventStore customerStore,
	IQueryableEventStore inventoryStore
) : PageModel
{
	[BindProperty(SupportsGet = true)]
	public string InventoryId { get; set; } = string.Empty;

	[BindProperty]
	public int Quantity { get; set; } = 1;

	[BindProperty]
	public string? ShippingAddress { get; set; }

	public CustomerAggregate? CurrentCustomer { get; private set; }

	public InventoryAggregate? InventoryItem { get; private set; }

	public decimal UnitPrice { get; private set; }

	public async Task<IActionResult> OnGetAsync()
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		var ct = HttpContext.RequestAborted;
		var currentCustomerResult = await customerStore.GetAsync<CustomerAggregate>(customerId, ct);
		CurrentCustomer = currentCustomerResult.IsSuccess ? currentCustomerResult.Value : null;
		var inventoryItemResult = await inventoryStore.GetAsync<InventoryAggregate>(InventoryId, ct);
		InventoryItem = inventoryItemResult.IsSuccess ? inventoryItemResult.Value : null;

		if (InventoryItem != null)
			UnitPrice = Math.Round(
				9.99m + (Math.Abs(InventoryItem.ProductId.GetHashCode(StringComparison.Ordinal)) % 9000 / 100m),
				2
			);

		return Page();
	}

	public async Task<IActionResult> OnPostAsync()
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		if (Quantity < 1)
		{
			ModelState.AddModelError(nameof(Quantity), "Quantity must be at least 1.");
			await ReloadAsync(customerId, HttpContext.RequestAborted);
			return Page();
		}

		var result = await fulfilmentService.PlaceOrderAsync(
			customerId,
			InventoryId,
			Quantity,
			ShippingAddress,
			HttpContext.RequestAborted
		);

		if (!result.Succeeded)
		{
			ModelState.AddModelError(string.Empty, result.ErrorMessage!);
			await ReloadAsync(customerId, HttpContext.RequestAborted);
			return Page();
		}

		var orderId = result.Order!.Id();
		TempData["Success"] = $"Order placed! Total: {result.Order.TotalAmount:C}";
		return RedirectToPage("/Customer/Orders/Details", new { id = orderId });
	}

	async Task ReloadAsync(string customerId, CancellationToken ct)
	{
		var currentCustomerResult = await customerStore.GetAsync<CustomerAggregate>(customerId, ct);
		CurrentCustomer = currentCustomerResult.IsSuccess ? currentCustomerResult.Value : null;
		var inventoryItemResult = await inventoryStore.GetAsync<InventoryAggregate>(InventoryId, ct);
		InventoryItem = inventoryItemResult.IsSuccess ? inventoryItemResult.Value : null;
		if (InventoryItem != null)
			UnitPrice = Math.Round(
				9.99m + (Math.Abs(InventoryItem.ProductId.GetHashCode(StringComparison.Ordinal)) % 9000 / 100m),
				2
			);
	}
}
