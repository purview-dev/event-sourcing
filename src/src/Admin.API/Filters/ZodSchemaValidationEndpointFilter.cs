using Microsoft.AspNetCore.Http;
using ZodSharp;
using ZodSharp.Core;

namespace Purview.EventSourcing.Admin.API.Filters;

/// <summary>
/// Validates a bound request argument using the ZodSharp source-generated schema for
/// <typeparamref name="T"/> and returns an RFC 7807 <see cref="ValidationProblem"/> when the request
/// fails validation.
/// </summary>
/// <typeparam name="T">The request contract type to validate.</typeparam>
/// <remarks>
/// <para>
/// The validator is resolved through <see cref="IZodSchemaFactory"/>, which the Admin API registers via
/// <c>AddZodSharp</c>. An optional refinement delegate can express cross-field rules that cannot be modelled
/// with per-property DataAnnotations (for example <c>FromUtc &lt;= ToUtc</c>).
/// </para>
/// <para>
/// When no validator is registered for <typeparamref name="T"/> the filter passes the request through
/// untouched so the endpoint remains functional if the factory is not wired up.
/// </para>
/// </remarks>
public sealed class ZodSchemaValidationEndpointFilter<T>(IZodSchemaFactory factory) : IEndpointFilter
{
	readonly IZodSchemaFactory _factory = factory;
	readonly Func<T, IEnumerable<ValidationError>>? _refinement;

	/// <summary>
	/// Initializes a new instance with a cross-field refinement delegate.
	/// </summary>
	/// <param name="factory">The ZodSharp schema factory.</param>
	/// <param name="refinement">
	/// Produces additional <see cref="ValidationError"/>s for cross-field rules, or an empty sequence when the
	/// request is valid.
	/// </param>
	public ZodSchemaValidationEndpointFilter(
		IZodSchemaFactory factory,
		Func<T, IEnumerable<ValidationError>> refinement
	)
		: this(factory) => _refinement = refinement;

	///<inheritdoc/>
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(next);

		var request = context.Arguments.OfType<T>().FirstOrDefault();
		if (request is not null)
		{
			var validator = _factory.Resolve<T>();
			if (validator is not null)
			{
				var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
				if (!result.IsSuccess || _refinement is not null)
				{
					List<ValidationError> errors = [];
					if (!result.IsSuccess)
						errors.AddRange(result.Errors);

					if (_refinement is not null)
						errors.AddRange(_refinement(request));

					if (errors.Count > 0)
					{
						var problem = ValidationResult<T>
							.Failure(errors)
							.ToHttpValidationProblemDetails(StatusCodes.Status400BadRequest);

						return TypedResults.ValidationProblem(problem.Errors, extensions: problem.Extensions);
					}
				}
			}
		}

		return await next(context);
	}
}

/// <summary>
/// Cross-field refinement rules shared by the Admin API request contracts.
/// </summary>
static class AdminContractRefinements
{
	/// <summary>
	/// Produces an error when <paramref name="from"/> is later than <paramref name="to"/>.
	/// </summary>
	public static IEnumerable<ValidationError> InvalidTimeRange(DateTimeOffset? from, DateTimeOffset? to, string field)
	{
		if (from is not null && to is not null && from.Value > to.Value)
		{
			yield return ValidationError.Create(
				"invalid_range",
				$"{field} must not be later than the upper bound.",
				[field]
			);
		}
	}

	/// <summary>
	/// Produces an error when <paramref name="from"/> is greater than <paramref name="to"/>.
	/// </summary>
	public static IEnumerable<ValidationError> InvalidVersionRange(long? from, long? to, string field)
	{
		if (from is not null && to is not null && from.Value > to.Value)
		{
			yield return ValidationError.Create(
				"invalid_range",
				$"{field} must not be greater than the upper bound.",
				[field]
			);
		}
	}

	/// <summary>
	/// Produces an error when a version bound is present but not a positive stream version.
	/// </summary>
	public static IEnumerable<ValidationError> InvalidVersionBound(long? version, string field)
	{
		if (version is not null && version.Value < 1)
		{
			yield return ValidationError.Create(
				"invalid_range",
				$"{field} must be a positive integer when specified.",
				[field]
			);
		}
	}
}
