using System.Globalization;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Purview.EventSourcing.Samples.Domain;

namespace Purview.EventSourcing.Samples.Web.Pages.Customer.Orders;

sealed class IndexModel(IQueryableEventStore customerStore, IQueryableEventStore orderStore) : PageModel
{
	const int DefaultPageSize = 15;

	[BindProperty(SupportsGet = true)]
	public new int Page { get; set; } = 1;

	[BindProperty(SupportsGet = true)]
	public int PageSize { get; set; } = DefaultPageSize;

	public CustomerAggregate? CurrentCustomer { get; private set; }
	public IReadOnlyList<OrderAggregate> Orders { get; private set; } = [];
	public long TotalCount { get; private set; }
	public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
	public bool HasPrevPage => Page > 1;
	public bool HasNextPage => Page < TotalPages;

	public async Task<IActionResult> OnGetAsync()
	{
		var customerId = HttpContext.Session.GetString("selectedCustomerId");
		if (string.IsNullOrEmpty(customerId))
			return RedirectToPage("/Customer/Index");

		if (Page < 1)
			Page = 1;
		if (PageSize is < 5 or > 100)
			PageSize = DefaultPageSize;

		var ct = HttpContext.RequestAborted;
		var currentCustomerResult = await customerStore.GetAsync<CustomerAggregate>(customerId, ct);
		CurrentCustomer = currentCustomerResult.IsSuccess ? currentCustomerResult.Value : null;

		var skipCount = (Page - 1) * PageSize;
		ContinuationRequest request = new()
		{
			ContinuationToken = skipCount > 0 ? $"{skipCount}" : null,
			MaxRecords = PageSize,
		};

		Expression<Func<OrderAggregate, bool>> where = o => o.CustomerId == customerId;
		TotalCount = await orderStore.CountAsync(where, ct);

		var result = await orderStore.QueryAsync(
			where,
			q => q.OrderByDescending(o => o.Details.SavedVersion),
			request,
			ct
		);
		Orders = result.Results;

		return Page();
	}

	public string PaginationLink(int page)
	{
		QueryBuilder query = new()
		{
			{ "page", page.ToString(CultureInfo.InvariantCulture) },
			{ "pageSize", PageSize.ToString(CultureInfo.InvariantCulture) },
		};

		return $"{HttpContext.Request.PathBase}{HttpContext.Request.Path}{query.ToQueryString()}";
	}
}
