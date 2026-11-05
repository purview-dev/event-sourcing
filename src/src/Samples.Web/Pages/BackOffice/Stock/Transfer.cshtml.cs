using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Services;

namespace Purview.EventSourcing.Samples.Web.Pages.BackOffice.Stock;

sealed class TransferModel(IStockTransferService transferService, IQueryableEventStore store) : PageModel
{
	[BindProperty]
	public string SourceInventoryId { get; set; } = string.Empty;

	[BindProperty]
	public string DestinationLocationId { get; set; } = string.Empty;

	[BindProperty]
	public int Quantity { get; set; } = 1;

	[BindProperty]
	public string Reason { get; set; } = string.Empty;

	public IReadOnlyList<SelectListItem> SourceInventoryItems { get; private set; } = [];
	public IReadOnlyList<SelectListItem> LocationItems { get; private set; } = [];

	public async Task OnGetAsync()
	{
		await LoadDropdownsAsync(HttpContext.RequestAborted);
	}

	public async Task<IActionResult> OnPostAsync()
	{
		var ct = HttpContext.RequestAborted;

		if (string.IsNullOrWhiteSpace(SourceInventoryId))
			ModelState.AddModelError(nameof(SourceInventoryId), "Please select stock to transfer.");
		if (string.IsNullOrWhiteSpace(DestinationLocationId))
			ModelState.AddModelError(nameof(DestinationLocationId), "Please select a destination location.");
		if (Quantity < 1)
			ModelState.AddModelError(nameof(Quantity), "Quantity must be at least 1.");
		if (string.IsNullOrWhiteSpace(Reason))
			ModelState.AddModelError(nameof(Reason), "A reason for the transfer is required.");

		if (!ModelState.IsValid)
		{
			await LoadDropdownsAsync(ct);
			return Page();
		}

		var result = await transferService.TransferAsync(
			SourceInventoryId,
			DestinationLocationId,
			Quantity,
			Reason.Trim(),
			ct
		);

		if (!result.Succeeded)
		{
			ModelState.AddModelError(string.Empty, result.ErrorMessage!);
			await LoadDropdownsAsync(ct);
			return Page();
		}

		TempData["Success"] =
			$"Transferred {result.Quantity} unit(s) of '{result.Source!.ProductName}' "
			+ $"from '{result.Source.LocationName}' to '{result.Destination!.LocationName}'.";
		return RedirectToPage("Index");
	}

	async Task LoadDropdownsAsync(CancellationToken ct)
	{
		var sourceItems = await store.QueryAsync<InventoryAggregate>(
			i => i.AvailableQuantity > 0,
			q => q.OrderBy(i => i.ProductName).ThenBy(i => i.LocationName),
			new ContinuationRequest { MaxRecords = 500 },
			ct
		);

		SourceInventoryItems =
		[
			.. sourceItems.Results.Select(i => new SelectListItem(
				$"{i.ProductName} — {i.LocationName} (available: {i.AvailableQuantity})",
				i.Id()
			)),
		];

		var locations = await store.ListAsync<LocationAggregate>(
			q => q.OrderBy(i => i.LocationName),
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
