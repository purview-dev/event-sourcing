using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Purview.EventSourcing.AzureKeyVault;

sealed class AzureKeyVaultEventCryptoKeyStoreOptionsValidator
	: IValidateOptions<AzureKeyVaultEventCryptoKeyStoreOptions>
{
	public ValidateOptionsResult Validate(string? name, AzureKeyVaultEventCryptoKeyStoreOptions options)
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
