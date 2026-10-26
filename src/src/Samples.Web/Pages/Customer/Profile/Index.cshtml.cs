using Microsoft.AspNetCore.Mvc;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Web.Infrastructure;

namespace Purview.EventSourcing.Samples.Web.Pages.Customer.Profile;

sealed class IndexModel(IQueryableEventStore store) : EventSourcingPageModel
{
	public CustomerAggregate? CurrentCustomer { get; private set; }

	public async Task<IActionResult> OnGetAsync()
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		var currentCustomerResult = await store.GetAsync<CustomerAggregate>(customerId, HttpContext.RequestAborted);
		CurrentCustomer = currentCustomerResult.IsSuccess ? currentCustomerResult.Value : null;
		return Page();
	}

	public async Task<IActionResult> OnPostChangeNameAsync(string newName)
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		if (string.IsNullOrWhiteSpace(newName))
		{
			TempData["Error"] = "Name cannot be empty.";
			return RedirectToPage();
		}

		var customerResult = await store.GetAsync<CustomerAggregate>(customerId, HttpContext.RequestAborted);
		var customer = customerResult.IsSuccess ? customerResult.Value : null;
		return customer == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					customer.ChangeName(newName.Trim());
					return await store.SaveAsync(customer, HttpContext.RequestAborted);
				},
				"Name updated.",
				RedirectToPage()
			);
	}

	public async Task<IActionResult> OnPostChangeEmailAsync(string newEmail)
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		var customerResult = await store.GetAsync<CustomerAggregate>(customerId, HttpContext.RequestAborted);
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
				RedirectToPage()
			);
	}

	public async Task<IActionResult> OnPostChangePhoneAsync(string? phoneNumber)
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		var customerResult = await store.GetAsync<CustomerAggregate>(customerId, HttpContext.RequestAborted);
		var customer = customerResult.IsSuccess ? customerResult.Value : null;
		return customer == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					customer.ChangePhoneNumber(string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim());
					return await store.SaveAsync(customer, HttpContext.RequestAborted);
				},
				"Phone number updated.",
				RedirectToPage()
			);
	}

	public async Task<IActionResult> OnPostUpdateAllAsync(string newName, string newEmail, string? phoneNumber)
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		if (string.IsNullOrWhiteSpace(newName))
		{
			TempData["Error"] = "Name cannot be empty.";
			return RedirectToPage();
		}

		if (string.IsNullOrWhiteSpace(newEmail))
		{
			TempData["Error"] = "Email cannot be empty.";
			return RedirectToPage();
		}

		var customerResult = await store.GetAsync<CustomerAggregate>(customerId, HttpContext.RequestAborted);
		var customer = customerResult.IsSuccess ? customerResult.Value : null;
		return customer == null
			? NotFound()
			: await TrySaveAsync(
				async () =>
				{
					customer.UpdateDetails(name: newName, email: newEmail, phoneNumber: phoneNumber);
					return await store.SaveAsync(customer, HttpContext.RequestAborted);
				},
				"Profile updated.",
				RedirectToPage()
			);
	}
}
