using Microsoft.AspNetCore.Mvc;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Services;

namespace Purview.EventSourcing.Samples.Web.Pages.Customer.Cart;

sealed class IndexModel(IQueryableEventStore customerStore, ICartCheckoutService checkoutService)
	: Microsoft.AspNetCore.Mvc.RazorPages.PageModel
{
	public CustomerAggregate? CurrentCustomer { get; private set; }
	public List<CartItem> CartItems { get; private set; } = [];
	public decimal CartTotal => CartItems.Sum(c => c.Quantity * c.UnitPrice);

	[BindProperty]
	public string? ShippingAddress { get; set; }

	public async Task<IActionResult> OnGetAsync()
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		CurrentCustomer = await customerStore.GetAsync<CustomerAggregate>(customerId, HttpContext.RequestAborted);
		CartItems = HttpContext.Session.GetCart();
		return Page();
	}

	public IActionResult OnPostUpdateQuantity(string inventoryId, int quantity)
	{
		var cart = HttpContext.Session.GetCart();
		var idx = cart.FindIndex(c => c.InventoryId == inventoryId);
		if (idx >= 0)
		{
			if (quantity <= 0)
				cart.RemoveAt(idx);
			else
				cart[idx] = cart[idx] with { Quantity = quantity };
		}

		HttpContext.Session.SetCart(cart);
		return RedirectToPage();
	}

	public IActionResult OnPostRemoveItem(string inventoryId)
	{
		var cart = HttpContext.Session.GetCart();
		cart.RemoveAll(c => c.InventoryId == inventoryId);
		HttpContext.Session.SetCart(cart);
		return RedirectToPage();
	}

	public IActionResult OnPostClearCart()
	{
		HttpContext.Session.SetCart([]);
		return RedirectToPage();
	}

	public async Task<IActionResult> OnPostCheckoutAsync()
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		var cart = HttpContext.Session.GetCart();
		if (cart.Count == 0)
		{
			TempData["Error"] = "Your cart is empty.";
			return RedirectToPage();
		}

		var result = await checkoutService.CheckoutAsync(customerId, cart, ShippingAddress, HttpContext.RequestAborted);

		if (!result.Succeeded)
		{
			TempData["Error"] = result.ErrorMessage;
			return RedirectToPage();
		}

		HttpContext.Session.SetCart([]);
		TempData["Success"] = "Order placed successfully!";
		return RedirectToPage("/Customer/Orders/Details", new { id = result.Order!.Id() });
	}
}
