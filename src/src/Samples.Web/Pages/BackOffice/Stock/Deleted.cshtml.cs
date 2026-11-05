using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Purview.EventSourcing.Samples.Domain;

namespace Purview.EventSourcing.Samples.Web.Pages.BackOffice.Stock;

sealed class DeletedModel(IQueryableEventStore store) : PageModel
{
	public IReadOnlyList<InventoryAggregate> DeletedItems { get; private set; } = [];

	public async Task OnGetAsync()
	{
		List<InventoryAggregate> deleted = [];
		await foreach (
			var id in store.GetAggregateIdsAsync<InventoryAggregate>(includeDeleted: true, HttpContext.RequestAborted)
		)
		{
			if ((await store.ExistsAsync<InventoryAggregate>(id, HttpContext.RequestAborted)).IsDeleted)
			{
				var aggregateResult = await store.GetDeletedAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
				var aggregate = aggregateResult.IsSuccess ? aggregateResult.Value : null;
				if (aggregate != null)
					deleted.Add(aggregate);
			}
		}

		DeletedItems = [.. deleted.OrderBy(i => i.ProductName)];
	}

	public async Task<IActionResult> OnPostRestoreAsync(string id)
	{
		var itemResult = await store.GetDeletedAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
		var item = itemResult.IsSuccess ? itemResult.Value : null;
		if (item == null)
			return NotFound();

		await store.RestoreAsync(item, HttpContext.RequestAborted);

		TempData["Success"] = $"'{item.ProductName}' restored.";
		return RedirectToPage("Deleted");
	}
}
