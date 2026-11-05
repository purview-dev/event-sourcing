namespace Purview.EventSourcing.Tenancy;

/// <summary>
/// Encodes a <see cref="TenantScope"/> into the single non-null column a side table keys on.
/// </summary>
/// <remarks>
/// <para>
/// Shared by every subsystem that stores rows keyed by tenant outside the event store itself -
/// subscription checkpoints, crypto keys - so they cannot drift apart. Two encodings of the same
/// concept is how one tenant's row ends up colliding with another's.
/// </para>
/// <para>
/// The system scope encodes as the empty string, which cannot collide with a real tenant because
/// <see cref="TenantScope.For(string)"/> rejects a blank identifier. Keeping the column non-null
/// also keeps a unique index free of NULLs, which matters because SQL Server and PostgreSQL
/// disagree about whether NULLs are distinct in one.
/// </para>
/// </remarks>
public static class TenantColumn
{
	/// <summary>The value stored for <see cref="TenantScope.System"/>.</summary>
	public const string SystemValue = "";

	/// <summary>Encodes a scope for storage.</summary>
	/// <param name="scope">The scope to encode.</param>
	public static string Encode(TenantScope scope) => scope.IsSystem ? SystemValue : scope.TenantId!;
}
