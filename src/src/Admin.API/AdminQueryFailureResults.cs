using Microsoft.AspNetCore.Http;
using Purview.EventSourcing.Admin.Failures;
using Purview.Results;

namespace Purview.EventSourcing.Admin.API;

/// <summary>
/// Maps an <see cref="AdminQueryFailure"/> onto its HTTP response.
/// </summary>
/// <remarks>
/// The whole table lives here, so no endpoint decides a status code and a new case has exactly one place to be
/// added. <c>Purview.Results.AspNetCore</c> would own this, but the published version's mapper is not part of
/// its public surface; adopt it once a release exposes <c>ToHttpResult</c>.
/// </remarks>
static class AdminQueryFailureResults
{
	/// <summary>
	/// Returns the successful value, or the response for the failure case.
	/// </summary>
	/// <typeparam name="TValue">The value the query produces.</typeparam>
	/// <param name="result">The query result.</param>
	/// <returns>An <see cref="IResult"/> carrying the value or the mapped failure.</returns>
	public static IResult ToHttpResult<TValue>(this Result<TValue, AdminQueryFailure> result) =>
		result.IsSuccess ? TypedResults.Ok(result.Value) : ToHttpResult(result.Error);

	/// <summary>
	/// Returns the response for a failure case.
	/// </summary>
	/// <param name="failure">The failure to map.</param>
	/// <returns>The mapped <see cref="IResult"/>.</returns>
	public static IResult ToHttpResult(this AdminQueryFailure failure) =>
		failure switch
		{
			AdminAggregateNotFound => TypedResults.NotFound(),
			_ => TypedResults.Problem(
				statusCode: StatusCodes.Status500InternalServerError,
				title: "The query failed with an error that is not mapped to a response."
			),
		};
}
