using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Purview.EventSourcing.Samples.Domain;

namespace Purview.EventSourcing.Samples.Web.Pages.BackOffice.Stock;

sealed class CreateModel(IQueryableEventStore store) : PageModel
{
	[BindProperty, Required, MaxLength(200)]
	public string ProductId { get; set; } = string.Empty;

	[BindProperty, Required, MaxLength(500)]
	public string ProductName { get; set; } = string.Empty;

	[BindProperty, Required, MaxLength(200)]
	public string LocationId { get; set; } = string.Empty;

	[BindProperty, Range(0, int.MaxValue)]
	public int InitialQuantity { get; set; }

	public IReadOnlyList<SelectListItem> LocationItems { get; private set; } = [];

	public async Task OnGetAsync()
	{
		await LoadLocationsAsync(HttpContext.RequestAborted);
	}

	public async Task<IActionResult> OnPostAsync()
	{
		var ct = HttpContext.RequestAborted;

		if (!ModelState.IsValid)
		{
			await LoadLocationsAsync(ct);
			return Page();
		}

		var productId = ProductId.Trim();
		var productName = ProductName.Trim();
		var locationId = LocationId.Trim();

		var locationResult = await store.GetAsync<LocationAggregate>(locationId, ct);
		var location = locationResult.IsSuccess ? locationResult.Value : null;
		if (location is null || location.Details.IsDeleted)
		{
			ModelState.AddModelError(nameof(LocationId), "Please select a valid physical location.");
			await LoadLocationsAsync(ct);
			return Page();
		}

		var existing = await store.FirstOrDefaultAsync<InventoryAggregate>(
			i => i.ProductId == productId && i.LocationId == locationId,
			ct
		);
		if (existing is not null)
		{
			ModelState.AddModelError(
				string.Empty,
				"Inventory for this product already exists at the selected location."
			);
			await LoadLocationsAsync(ct);
			return Page();
		}

		var item = await store.CreateAsync<InventoryAggregate>(cancellationToken: ct);
		item.Create(productId, productName, location.LocationId, location.LocationName, InitialQuantity);
		await store.SaveAsync(item, ct);

		TempData["Success"] = $"Stock item '{item.ProductName}' created.";
		return RedirectToPage("Index");
	}

	async Task LoadLocationsAsync(CancellationToken ct)
	{
		var locations = await store.ListAsync<LocationAggregate>(
			q => q.OrderBy(location => location.LocationName),
			new ContinuationRequest { MaxRecords = 500 },
			ct
		);

		LocationItems =
		[
			.. locations.Results.Select(location => new SelectListItem(
				$"{location.LocationName} ({location.LocationId})",
				location.LocationId
			)),
		];
	}
}
