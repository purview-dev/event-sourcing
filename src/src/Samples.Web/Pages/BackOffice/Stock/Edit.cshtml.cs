using Microsoft.AspNetCore.Mvc;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Web.Infrastructure;

namespace Purview.EventSourcing.Samples.Web.Pages.BackOffice.Stock;

sealed class EditModel(IQueryableEventStore store) : EventSourcingPageModel
{
	public InventoryAggregate? Item { get; private set; }

	public async Task OnGetAsync(string id)
	{
		var itemResult = await store.GetAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
		Item = itemResult.IsSuccess ? itemResult.Value : null;
	}

	public async Task<IActionResult> OnPostReceiveStockAsync(string id, int quantity)
	{
		var itemResult = await store.GetAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
		var item = itemResult.IsSuccess ? itemResult.Value : null;
		return item == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					item.ReceiveStock(quantity);
					return await store.SaveAsync(item, HttpContext.RequestAborted);
				},
				$"Received {quantity} unit(s).",
				RedirectToPage(new { id })
			);
	}

	public async Task<IActionResult> OnPostAdjustStockAsync(string id, int newQuantity, string reason)
	{
		var itemResult = await store.GetAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
		var item = itemResult.IsSuccess ? itemResult.Value : null;
		return item == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					item.AdjustStock(newQuantity, reason.Trim());
					return await store.SaveAsync(item, HttpContext.RequestAborted);
				},
				$"Stock adjusted to {newQuantity}.",
				RedirectToPage(new { id })
			);
	}

	public async Task<IActionResult> OnPostReserveStockAsync(string id, int quantity, string orderId)
	{
		var itemResult = await store.GetAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
		var item = itemResult.IsSuccess ? itemResult.Value : null;
		return item == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					item.ReserveStock(quantity, orderId.Trim());
					return await store.SaveAsync(item, HttpContext.RequestAborted);
				},
				$"Reserved {quantity} unit(s) for order {orderId}.",
				RedirectToPage(new { id })
			);
	}

	public async Task<IActionResult> OnPostReleaseReservationAsync(string id, int quantity, string orderId)
	{
		var itemResult = await store.GetAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
		var item = itemResult.IsSuccess ? itemResult.Value : null;
		return item == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					item.ReleaseReservation(quantity, orderId.Trim());
					return await store.SaveAsync(item, HttpContext.RequestAborted);
				},
				$"Released {quantity} unit(s) for order {orderId}.",
				RedirectToPage(new { id })
			);
	}

	public async Task<IActionResult> OnPostShipStockAsync(string id, int quantity, string orderId)
	{
		var itemResult = await store.GetAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
		var item = itemResult.IsSuccess ? itemResult.Value : null;
		return item == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					item.ShipStock(quantity, orderId.Trim());
					return await store.SaveAsync(item, HttpContext.RequestAborted);
				},
				$"Shipped {quantity} unit(s) for order {orderId}.",
				RedirectToPage(new { id })
			);
	}
}
