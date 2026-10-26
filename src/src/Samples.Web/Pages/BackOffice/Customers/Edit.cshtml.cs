using Microsoft.AspNetCore.Mvc;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Web.Infrastructure;

namespace Purview.EventSourcing.Samples.Web.Pages.BackOffice.Customers;

sealed class EditModel(IQueryableEventStore store) : EventSourcingPageModel
{
	public CustomerAggregate? Customer { get; private set; }

	public async Task OnGetAsync(string id)
	{
		var customerResult = await store.GetAsync<CustomerAggregate>(id, HttpContext.RequestAborted);
		Customer = customerResult.IsSuccess ? customerResult.Value : null;
	}

	public async Task<IActionResult> OnPostChangeNameAsync(string id, string newName)
	{
		if (string.IsNullOrWhiteSpace(newName))
		{
			TempData["Error"] = "Name cannot be empty.";
			return RedirectToPage(new { id });
		}

		var customerResult = await store.GetAsync<CustomerAggregate>(id, HttpContext.RequestAborted);
		var customer = customerResult.IsSuccess ? customerResult.Value : null;
		return customer == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					customer.ChangeName(newName);
					return await store.SaveAsync(customer, HttpContext.RequestAborted);
				},
				"Name updated.",
				RedirectToPage(new { id })
			);
	}

	public async Task<IActionResult> OnPostChangeEmailAsync(string id, string newEmail)
	{
		var customerResult = await store.GetAsync<CustomerAggregate>(id, HttpContext.RequestAborted);
		var customer = customerResult.IsSuccess ? customerResult.Value : null;
		return customer == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					customer.ChangeEmail(newEmail);
					return await store.SaveAsync(customer, HttpContext.RequestAborted);
				},
				"Email updated.",
				RedirectToPage(new { id })
			);
	}

	public async Task<IActionResult> OnPostChangePhoneAsync(string id, string? phoneNumber)
	{
		var customerResult = await store.GetAsync<CustomerAggregate>(id, HttpContext.RequestAborted);
		var customer = customerResult.IsSuccess ? customerResult.Value : null;
		return customer == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					customer.ChangePhoneNumber(phoneNumber);
					return await store.SaveAsync(customer, HttpContext.RequestAborted);
				},
				"Phone number updated.",
				RedirectToPage(new { id })
			);
	}

	public async Task<IActionResult> OnPostDeactivateAsync(string id)
	{
		var customerResult = await store.GetAsync<CustomerAggregate>(id, HttpContext.RequestAborted);
		var customer = customerResult.IsSuccess ? customerResult.Value : null;
		return customer == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					customer.Deactivate();
					return await store.SaveAsync(customer, HttpContext.RequestAborted);
				},
				"Customer deactivated.",
				RedirectToPage(new { id })
			);
	}

	public async Task<IActionResult> OnPostReactivateAsync(string id)
	{
		var customerResult = await store.GetAsync<CustomerAggregate>(id, HttpContext.RequestAborted);
		var customer = customerResult.IsSuccess ? customerResult.Value : null;
		return customer == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					customer.Reactivate();
					return await store.SaveAsync(customer, HttpContext.RequestAborted);
				},
				"Customer reactivated.",
				RedirectToPage(new { id })
			);
	}
}
