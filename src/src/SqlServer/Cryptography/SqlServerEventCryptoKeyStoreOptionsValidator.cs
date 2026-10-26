using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Purview.EventSourcing.SqlServer.Cryptography;

sealed class SqlServerEventCryptoKeyStoreOptionsValidator : IValidateOptions<SqlServerEventCryptoKeyStoreOptions>
{
	public ValidateOptionsResult Validate(string? name, SqlServerEventCryptoKeyStoreOptions options)
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
