using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Purview.EventSourcing.Admin.Client;

namespace Purview.EventSourcing.Admin.Site.Pages;

/// <summary>
/// Page model for the admin portal event history page.
/// </summary>
/// <param name="adminApiClient">The generated Admin API client used to load event ranges.</param>
sealed class EventsModel(AdminApiClient adminApiClient) : PageModel
{
	readonly AdminApiClient _adminAPIClient = adminApiClient ?? throw new ArgumentNullException(nameof(adminApiClient));

	/// <summary>
	/// Gets or sets the aggregate type whose events are displayed.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string AggregateType { get; set; } = default!;

	/// <summary>
	/// Gets or sets the aggregate identifier whose events are displayed.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string AggregateId { get; set; } = default!;

	/// <summary>
	/// Gets or sets the inclusive lower bound of the stream version to display.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public long? VersionFrom { get; set; }

	/// <summary>
	/// Gets or sets the inclusive upper bound of the stream version to display.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public long? VersionTo { get; set; }

	/// <summary>
	/// Gets or sets the inclusive lower bound of the event timestamp (UTC) to display.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public DateTime? TimeFromUtc { get; set; }

	/// <summary>
	/// Gets or sets the inclusive upper bound of the event timestamp (UTC) to display.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public DateTime? TimeToUtc { get; set; }

	/// <summary>
	/// Gets or sets the one-based page number to display.
	/// </summary>
	[BindProperty(SupportsGet = true, Name = "pageNo")]
	public int PageNo { get; set; } = 1;

	/// <summary>
	/// Gets or sets the number of events to show per page.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public int PageSize { get; set; } = 25;

	/// <summary>
	/// Gets or sets the event range to render on the page.
	/// </summary>
	public PagedResultOfEventEnvelopeResponse? EventRange { get; set; }

	/// <summary>
	/// Handles GET requests for the page by loading the requested event range through the Admin API client.
	/// </summary>
	/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
	/// <returns>The page result, or a bad request when the required parameters are missing.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(AggregateType) || string.IsNullOrWhiteSpace(AggregateId))
		{
			return BadRequest("AggregateType and AggregateId are required.");
		}

		try
		{
			EventRange = await _adminAPIClient.GetAggregateEventRangeAsync(
				AggregateType,
				AggregateId,
				VersionFrom,
				VersionTo,
				TimeFromUtc is not null ? new DateTimeOffset(TimeFromUtc.Value, TimeSpan.Zero) : null,
				TimeToUtc is not null ? new DateTimeOffset(TimeToUtc.Value, TimeSpan.Zero) : null,
				PageNo,
				PageSize,
				"Version asc",
				cancellationToken
			);

			return Page();
		}
		catch (AdminApiException ex)
		{
			ModelState.AddModelError(string.Empty, $"Failed to load events: {ex.Message}");
			return Page();
		}
		catch (HttpRequestException ex)
		{
			ModelState.AddModelError(string.Empty, $"Failed to load events: {ex.Message}");
			return Page();
		}
	}
}
