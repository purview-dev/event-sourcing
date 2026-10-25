using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Purview.EventSourcing.Admin.Client;

namespace Purview.EventSourcing.Admin.Site.Pages;

/// <summary>
/// Page model for the admin portal point-in-time projection page.
/// </summary>
/// <param name="adminApiClient">The generated Admin API client used to build projections.</param>
sealed class ProjectionModel(AdminApiClient adminApiClient) : PageModel
{
	readonly AdminApiClient _adminAPIClient = adminApiClient ?? throw new ArgumentNullException(nameof(adminApiClient));

	/// <summary>
	/// Gets or sets the aggregate type to project.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string AggregateType { get; set; } = default!;

	/// <summary>
	/// Gets or sets the aggregate identifier to project.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string AggregateId { get; set; } = default!;

	/// <summary>
	/// Gets or sets the stream version to project at, when a version-based projection is requested.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public long? Version { get; set; }

	/// <summary>
	/// Gets or sets the UTC timestamp to project at, when a time-based projection is requested.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public DateTime? AsOfUtc { get; set; }

	/// <summary>
	/// Gets or sets the projected aggregate state to render on the page.
	/// </summary>
	public ProjectionResponse? Projection { get; set; }

	/// <summary>
	/// Handles GET requests for the page by building the requested projection through the Admin API client.
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
			Projection =
				Version.HasValue && Version.Value > 0
					? await _adminAPIClient.GetAggregateProjectionAtVersionAsync(
						AggregateType,
						AggregateId,
						Version,
						cancellationToken
					)
				: AsOfUtc.HasValue
					? await _adminAPIClient.GetAggregateProjectionAtTimeAsync(
						AggregateType,
						AggregateId,
						new DateTimeOffset(AsOfUtc.Value, TimeSpan.Zero),
						cancellationToken
					)
				: await _adminAPIClient.GetAggregateProjectionAtVersionAsync(
					AggregateType,
					AggregateId,
					long.MaxValue,
					cancellationToken
				);

			return Page();
		}
		catch (AdminApiException ex)
		{
			ModelState.AddModelError(string.Empty, $"Failed to load projection: {ex.Message}");
			return Page();
		}
		catch (HttpRequestException ex)
		{
			ModelState.AddModelError(string.Empty, $"Failed to load projection: {ex.Message}");
			return Page();
		}
	}
}
