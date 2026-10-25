using Microsoft.AspNetCore.Mvc;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.ValueObjects;
using Purview.EventSourcing.Samples.Web.Infrastructure;

namespace Purview.EventSourcing.Samples.Web.Pages.Customer.Orders;

sealed class DetailsModel(IQueryableEventStore customerStore, IQueryableEventStore orderStore) : EventSourcingPageModel
{
	public OrderAggregate? Order { get; private set; }
	public CustomerAggregate? CurrentCustomer { get; private set; }

	public async Task<IActionResult> OnGetAsync(string id)
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		var ct = HttpContext.RequestAborted;
		var currentCustomerResult = await customerStore.GetAsync<CustomerAggregate>(customerId, ct);
		CurrentCustomer = currentCustomerResult.IsSuccess ? currentCustomerResult.Value : null;
		var orderResult = await orderStore.GetAsync<OrderAggregate>(id, ct);
		Order = orderResult.IsSuccess ? orderResult.Value : null;

		return Order == null || Order.CustomerId != customerId ? NotFound() : Page();
	}

	public async Task<IActionResult> OnPostAddLineItemAsync(
		string id,
		string productId,
		string productName,
		int quantity,
		decimal unitPrice
	)
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		var orderResult = await orderStore.GetAsync<OrderAggregate>(id, HttpContext.RequestAborted);
		var order = orderResult.IsSuccess ? orderResult.Value : null;
		if (order == null || order.CustomerId != customerId)
			return NotFound();
		if (order.Status != OrderStatusCode.Draft)
		{
			TempData["Error"] = "Only draft orders can be edited.";
			return RedirectToPage(new { id });
		}

		return await TrySaveAsync(
			async () =>
			{
				order.AddLineItem(productId.Trim(), productName.Trim(), quantity, unitPrice);
				return await orderStore.SaveAsync(order, HttpContext.RequestAborted);
			},
			"Line item added.",
			RedirectToPage(new { id })
		);
	}

	public async Task<IActionResult> OnPostRemoveLineItemAsync(string id, string productId)
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		var orderResult = await orderStore.GetAsync<OrderAggregate>(id, HttpContext.RequestAborted);
		var order = orderResult.IsSuccess ? orderResult.Value : null;
		if (order == null || order.CustomerId != customerId)
			return NotFound();
		if (order.Status != OrderStatus.Draft)
		{
			TempData["Error"] = "Only draft orders can be edited.";
			return RedirectToPage(new { id });
		}

		return await TrySaveAsync(
			async () =>
			{
				order.RemoveLineItem(productId);
				return await orderStore.SaveAsync(order, HttpContext.RequestAborted);
			},
			"Line item removed.",
			RedirectToPage(new { id })
		);
	}

	public async Task<IActionResult> OnPostSetShippingAddressAsync(string id, string address)
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		var orderResult = await orderStore.GetAsync<OrderAggregate>(id, HttpContext.RequestAborted);
		var order = orderResult.IsSuccess ? orderResult.Value : null;
		if (order == null || order.CustomerId != customerId)
			return NotFound();
		if (order.Status != OrderStatus.Draft)
		{
			TempData["Error"] = "Only draft orders can be edited.";
			return RedirectToPage(new { id });
		}

		return await TrySaveAsync(
			async () =>
			{
				order.SetShippingAddress(address);
				return await orderStore.SaveAsync(order, HttpContext.RequestAborted);
			},
			"Shipping address updated.",
			RedirectToPage(new { id })
		);
	}

	public async Task<IActionResult> OnPostUpdateNotesAsync(string id, string? notes)
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		var orderResult = await orderStore.GetAsync<OrderAggregate>(id, HttpContext.RequestAborted);
		var order = orderResult.IsSuccess ? orderResult.Value : null;
		if (order == null || order.CustomerId != customerId)
			return NotFound();
		if (order.Status != OrderStatus.Draft)
		{
			TempData["Error"] = "Only draft orders can be edited.";
			return RedirectToPage(new { id });
		}

		return await TrySaveAsync(
			async () =>
			{
				order.UpdateNotes(string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
				return await orderStore.SaveAsync(order, HttpContext.RequestAborted);
			},
			"Notes updated.",
			RedirectToPage(new { id })
		);
	}

	public async Task<IActionResult> OnPostCancelAsync(string id)
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		var orderResult = await orderStore.GetAsync<OrderAggregate>(id, HttpContext.RequestAborted);
		var order = orderResult.IsSuccess ? orderResult.Value : null;
		if (order == null || order.CustomerId != customerId)
			return NotFound();
		if (order.Status != OrderStatus.Draft)
		{
			TempData["Error"] = "Only draft orders can be cancelled by customers.";
			return RedirectToPage(new { id });
		}

		return await TrySaveAsync(
			async () =>
			{
				order.CancelOrder();
				return await orderStore.SaveAsync(order, HttpContext.RequestAborted);
			},
			"Order cancelled.",
			RedirectToPage(new { id })
		);
	}
}
