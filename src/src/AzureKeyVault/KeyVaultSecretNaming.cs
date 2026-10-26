using System.Security.Cryptography;
using System.Text;

namespace Purview.EventSourcing.AzureKeyVault;

/// <summary>
/// Derives the Key Vault secret name that holds a tenant's key for one data subject.
/// </summary>
/// <remarks>
/// <para>
/// A vault secret name may contain only <c>0-9</c>, <c>a-z</c>, <c>A-Z</c> and <c>-</c>, and is
/// limited to 127 characters. That rules out Base64Url, which uses <c>_</c>, and rules out simply
/// concatenating a tenant and a subject identifier, which can exceed the limit.
/// </para>
/// <para>
/// The name is a SHA-256 of the tenant and subject instead: always legal, always the same length,
/// and collision-free in practice. It also keeps the subject identifier out of the vault's name,
/// which appears in portal listings and diagnostic logs that are visible far more widely than the
/// key itself. The tenant and subject are written to the secret's tags, so an operator can still
/// identify a secret when they legitimately need to.
/// </para>
/// </remarks>
static class KeyVaultSecretNaming
{
	/// <summary>The tag recording the owning tenant, or empty for the system scope.</summary>
	public const string TenantTag = "purview-tenant";

	/// <summary>The tag recording the data subject.</summary>
	public const string DataSubjectTag = "purview-data-subject";

	/// <summary>
	/// Builds the secret name for a tenant and subject.
	/// </summary>
	/// <param name="prefix">The configured name prefix, letting one vault serve several deployments.</param>
	/// <param name="tenantId">The encoded tenant, empty for the system scope.</param>
	/// <param name="dataSubjectId">The data subject.</param>
	public static string NameFor(string prefix, string tenantId, string dataSubjectId)
	{
		// The separator is inside the hashed input so a tenant ending in it cannot produce the same
		// name as a subject starting with it.
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}\u001f{dataSubjectId}"));

		return $"{prefix}-{Convert.ToHexStringLower(hash)}";
	}
}
