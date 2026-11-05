using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Purview.EventSourcing.MongoDB.Subscriptions;

sealed class MongoDBSubscriptionCheckpointStoreOptionsValidator
	: IValidateOptions<MongoDBSubscriptionCheckpointStoreOptions>
{
	public ValidateOptionsResult Validate(string? name, MongoDBSubscriptionCheckpointStoreOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		ValidationContext validationContext = new(options);
		List<ValidationResult> validationResults = [];
		if (!Validator.TryValidateObject(options, validationContext, validationResults, validateAllProperties: true))
			return ValidateOptionsResult.Fail(
				validationResults.Select(static x => x.ErrorMessage ?? "Options validation failed.")
			);

		return ValidateOptionsResult.Success;
	}
}
