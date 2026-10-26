using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Purview.EventSourcing.Admin.Client;

namespace Purview.EventSourcing.Admin.Site.Pages;

/// <summary>
/// Page model for the admin portal home page that searches for aggregates.
/// </summary>
/// <param name="adminApiClient">The generated Admin API client used to search aggregates.</param>
sealed class AdminIndexModel(AdminApiClient adminApiClient) : PageModel
{
	readonly AdminApiClient _adminAPIClient = adminApiClient ?? throw new ArgumentNullException(nameof(adminApiClient));

	/// <summary>
	/// Gets or sets the aggregate type filter for the search.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string? AggregateType { get; set; }

	/// <summary>
	/// Gets or sets the aggregate identifier filter for the search.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string? AggregateId { get; set; }

	/// <summary>
	/// Gets or sets the one-based page number to display.
	/// </summary>
	[BindProperty(SupportsGet = true, Name = "pageNo")]
	public int PageNo { get; set; } = 1;

	/// <summary>
	/// Gets or sets the number of results to show per page.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public int PageSize { get; set; } = 25;

	/// <summary>
	/// Gets or sets the search results to render on the page.
	/// </summary>
	public PagedResultOfAggregateSummaryResponse? SearchResults { get; set; }

	/// <summary>
	/// Handles GET requests for the page by executing an aggregate search through the Admin API client.
	/// </summary>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>The page result.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
	{
		try
		{
			SearchResults = await _adminAPIClient.SearchAggregatesAsync(
				new AggregateSearchRequest
				{
					AggregateType = string.IsNullOrWhiteSpace(AggregateType) ? null : AggregateType,
					AggregateId = string.IsNullOrWhiteSpace(AggregateId) ? null : AggregateId,
					Page = PageNo,
					PageSize = PageSize,
				},
				cancellationToken
			);

			return Page();
		}
		catch (AdminApiException ex)
		{
			ModelState.AddModelError(string.Empty, $"Search failed: {ex.Message}");
			return Page();
		}
		catch (HttpRequestException ex)
		{
			ModelState.AddModelError(string.Empty, $"Search failed: {ex.Message}");
			return Page();
		}
	}
}
