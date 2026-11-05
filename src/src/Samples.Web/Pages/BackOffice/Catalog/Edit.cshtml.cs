using Microsoft.AspNetCore.Mvc;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Web.Infrastructure;
using Purview.EventSourcing.Samples.Web.Services;

namespace Purview.EventSourcing.Samples.Web.Pages.BackOffice.Catalog;

sealed class EditModel(IQueryableEventStore store, IProductImageService imageService) : EventSourcingPageModel
{
	public InventoryAggregate? Item { get; private set; }
	public string? CurrentImageUrl { get; private set; }

	public async Task OnGetAsync(string id)
	{
		var itemResult = await store.GetAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
		Item = itemResult.IsSuccess ? itemResult.Value : null;
		if (Item != null)
			CurrentImageUrl = await imageService.GetImageUrlAsync(Item.ProductId, HttpContext.RequestAborted);
	}

	public async Task<IActionResult> OnPostUpdateProductNameAsync(string id, string productName)
	{
		var itemResult = await store.GetAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
		var item = itemResult.IsSuccess ? itemResult.Value : null;
		return item == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					item.UpdateDetails(productName: productName.Trim());
					return await store.SaveAsync(item, HttpContext.RequestAborted);
				},
				"Product name updated.",
				RedirectToPage(new { id })
			);
	}

	public async Task<IActionResult> OnPostUploadImageAsync(string id, IFormFile? imageFile)
	{
		if (imageFile == null || imageFile.Length == 0)
		{
			TempData["Error"] = "Please select an image file.";
			return RedirectToPage(new { id });
		}

		var itemResult = await store.GetAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
		var item = itemResult.IsSuccess ? itemResult.Value : null;
		if (item == null)
			return NotFound();

		var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
		if (!allowedTypes.Contains(imageFile.ContentType, StringComparer.OrdinalIgnoreCase))
		{
			TempData["Error"] = "Only JPEG, PNG, WebP or GIF images are allowed.";
			return RedirectToPage(new { id });
		}

		await using var stream = imageFile.OpenReadStream();
		await imageService.UploadImageAsync(item.ProductId, stream, imageFile.ContentType, HttpContext.RequestAborted);

		TempData["Success"] = "Image uploaded.";
		return RedirectToPage(new { id });
	}

	public async Task<IActionResult> OnPostDeleteImageAsync(string id)
	{
		var itemResult = await store.GetAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
		var item = itemResult.IsSuccess ? itemResult.Value : null;
		if (item == null)
			return NotFound();

		await imageService.DeleteImageAsync(item.ProductId, HttpContext.RequestAborted);

		TempData["Success"] = "Image removed.";
		return RedirectToPage(new { id });
	}
}
