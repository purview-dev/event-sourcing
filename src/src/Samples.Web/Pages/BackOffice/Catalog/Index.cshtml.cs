using System.Globalization;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Purview.EventSourcing.Samples.Domain;

namespace Purview.EventSourcing.Samples.Web.Pages.BackOffice.Catalog;

sealed class IndexModel(IQueryableEventStore store) : PageModel
{
	const int DefaultPageSize = 15;

	[BindProperty(SupportsGet = true)]
	public string? Search { get; set; }

	[BindProperty(SupportsGet = true)]
	public new int Page { get; set; } = 1;

	[BindProperty(SupportsGet = true)]
	public int PageSize { get; set; } = DefaultPageSize;

	[BindProperty(SupportsGet = true)]
	public string SortBy { get; set; } = "name";

	[BindProperty(SupportsGet = true)]
	public string SortDir { get; set; } = "asc";

	public IReadOnlyList<InventoryAggregate> Items { get; private set; } = [];
	public long TotalCount { get; private set; }
	public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
	public bool HasPrevPage => Page > 1;
	public bool HasNextPage => Page < TotalPages;

	public async Task OnGetAsync()
	{
		if (Page < 1)
			Page = 1;
		if (PageSize is < 5 or > 100)
			PageSize = DefaultPageSize;

		var ct = HttpContext.RequestAborted;
		var skipCount = (Page - 1) * PageSize;
		ContinuationRequest request = new()
		{
			ContinuationToken = skipCount > 0 ? $"{skipCount}" : null,
			MaxRecords = PageSize,
		};

#pragma warning disable CA1862 // Use the 'StringComparison' method overloads to perform case-insensitive string comparisons
#pragma warning disable CA1308 // Normalize strings to uppercase
		var search = Search?.Trim().ToLowerInvariant() ?? string.Empty;
		var hasFilter = !string.IsNullOrEmpty(search);

		Expression<Func<InventoryAggregate, bool>> where = i =>
			i.ProductId.ToLowerInvariant().Contains(search) || i.ProductName.ToLowerInvariant().Contains(search);
#pragma warning restore CA1308 // Normalize strings to uppercase
#pragma warning restore CA1862 // Use the 'StringComparison' method overloads to perform case-insensitive string comparisons

		Func<IQueryable<InventoryAggregate>, IQueryable<InventoryAggregate>> orderBy = (SortBy, SortDir) switch
		{
			("productid", "desc") => q => q.OrderByDescending(i => i.ProductId),
			("productid", _) => q => q.OrderBy(i => i.ProductId),
			("location", "desc") => q => q.OrderByDescending(i => i.LocationName),
			("location", _) => q => q.OrderBy(i => i.LocationName),
			("name", "desc") => q => q.OrderByDescending(i => i.ProductName),
			_ => q => q.OrderBy(i => i.ProductName),
		};

		TotalCount = await store.CountAsync(hasFilter ? where : null, ct);

		var result = hasFilter
			? await store.QueryAsync(where, orderBy, request, ct)
			: await store.ListAsync(orderBy, request, ct);
		Items = result.Results;
	}

	public async Task<IActionResult> OnPostArchiveAsync(string id)
	{
		var itemResult = await store.GetAsync<InventoryAggregate>(id, HttpContext.RequestAborted);
		var item = itemResult.IsSuccess ? itemResult.Value : null;
		if (item != null)
		{
			await store.DeleteAsync(item, HttpContext.RequestAborted);
			TempData["Success"] = $"'{item.ProductName}' archived.";
		}

		return RedirectToPage("Index");
	}

	public string PaginationLink(int page)
	{
		QueryBuilder query = new()
		{
			{ "sortBy", SortBy },
			{ "sortDir", SortDir },
			{ "page", page.ToString(CultureInfo.InvariantCulture) },
			{ "pageSize", PageSize.ToString(CultureInfo.InvariantCulture) },
		};

		if (!string.IsNullOrWhiteSpace(Search))
			query.Add("search", Search);

		return $"{HttpContext.Request.PathBase}{HttpContext.Request.Path}{query.ToQueryString()}";
	}

	public string SortLink(string column) => column == SortBy && SortDir == "asc" ? "desc" : "asc";

	public string SortIcon(string column) =>
		SortBy != column ? "↕"
		: SortDir == "asc" ? "↑"
		: "↓";
}
