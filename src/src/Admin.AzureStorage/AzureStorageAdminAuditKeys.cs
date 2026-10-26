using System.Globalization;

namespace Purview.EventSourcing.Admin.AzureStorage;

/// <summary>
/// How an audit entry is addressed in Azure Table Storage.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The tenant is the partition key, not a predicate.</strong> Structural isolation is
/// preferred over a filter wherever the store allows it: a predicate is something a future query can
/// forget, whereas a partition a query never names cannot be returned by accident.
/// </para>
/// <para>
/// Real tenants are prefixed so they cannot collide with the unowned partition. A tenant literally
/// named <c>~unowned</c> would otherwise read every unstamped entry in the account.
/// </para>
/// </remarks>
static class AzureStorageAdminAuditKeys
{
	/// <summary>Holds entries written with no tenant - visible only to the system scope.</summary>
	public const string UnownedPartition = "~unowned";

	public static string PartitionFor(string? tenantId) =>
		string.IsNullOrWhiteSpace(tenantId) ? UnownedPartition : $"t:{tenantId}";

	/// <summary>
	/// Inverted ticks, so Table Storage's ascending row-key order <em>is</em> newest-first within a
	/// partition and the reader needs no sort for a tenant-scoped read.
	/// </summary>
	/// <remarks>
	/// The suffix disambiguates entries written inside one tick. Without it two audit entries written
	/// in the same instant would collide on the row key, and the second insert would fail - losing
	/// the entry under the failure mode that is supposed to be the safest.
	/// </remarks>
	public static string RowKeyFor(DateTimeOffset timestamp) =>
		string.Create(
			CultureInfo.InvariantCulture,
			$"{(DateTimeOffset.MaxValue.Ticks - timestamp.UtcTicks).ToString("D19", CultureInfo.InvariantCulture)}_{Guid.NewGuid():N}"
		);

	/// <summary>
	/// The row key a timestamp maps to, for a range filter. Inverted ticks reverse the comparison:
	/// a <em>later</em> time produces a <em>smaller</em> key.
	/// </summary>
	public static string RowKeyBoundFor(DateTimeOffset timestamp) =>
		(DateTimeOffset.MaxValue.Ticks - timestamp.UtcTicks).ToString("D19", CultureInfo.InvariantCulture);
}
