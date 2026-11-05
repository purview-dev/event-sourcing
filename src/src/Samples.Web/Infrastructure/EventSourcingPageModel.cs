using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.Samples.Web.Infrastructure;

/// <summary>
/// Base page model that provides a helper for wrapping event-store saves with
/// typed failure handling, surfaced as a user-friendly TempData message rather
/// than an unhandled 500.
/// </summary>
abstract class EventSourcingPageModel : PageModel
{
	const string ConflictMessage =
		"This record was modified by another user while you were editing it. "
		+ "The page has been refreshed — please review and resubmit your change.";

	/// <summary>
	/// Executes <paramref name="saveAction"/>, sets a success or failure message,
	/// and returns <paramref name="result"/> in both cases.
	/// </summary>
	/// <remarks>
	/// The save returns its failure rather than throwing, so the outcome has to be inspected: a page
	/// that ignored it would report success for a write that never happened.
	/// </remarks>
	protected async Task<IActionResult> TrySaveAsync<TAggregate>(
		Func<Task<Result<SaveOutcome<TAggregate>, AggregateSaveFailure>>> saveAction,
		string successMessage,
		IActionResult result
	)
		where TAggregate : class, IAggregate, new()
	{
		var saveResult = await saveAction();
		if (saveResult.IsSuccess)
		{
			TempData["Success"] = successMessage;
			return result;
		}

		TempData["Error"] = saveResult.Error switch
		{
			ConcurrencyConflict => ConflictMessage,
			AggregateLocked => "This record is locked and cannot be changed.",
			AggregateDeleted => "This record has been deleted.",
			AggregateInvalid invalid => string.Join(
				" ",
				invalid.ValidationResult.Failures.Select(failure => failure.ErrorMessage)
			),
			_ => "The change could not be saved.",
		};

		return result;
	}
}
