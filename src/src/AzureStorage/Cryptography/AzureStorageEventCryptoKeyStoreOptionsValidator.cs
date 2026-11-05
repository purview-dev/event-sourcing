using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Purview.EventSourcing.AzureStorage.Cryptography;

sealed class AzureStorageEventCryptoKeyStoreOptionsValidator : IValidateOptions<AzureStorageEventCryptoKeyStoreOptions>
{
	public ValidateOptionsResult Validate(string? name, AzureStorageEventCryptoKeyStoreOptions options)
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
