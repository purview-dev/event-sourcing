using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Encodes a <see cref="TenantScope"/> into the single non-null column a checkpoint store keys on.
/// </summary>
/// <remarks>
/// <para>
/// Shared so every provider encodes the scope identically. A store that invented its own encoding
/// could make one tenant's position collide with another's, or with the system scope's, which would
/// advance a position past events that are then never delivered.
/// </para>
/// <para>
/// The system scope encodes as the empty string. That cannot collide with a real tenant, because
/// <see cref="TenantScope.For(string)"/> rejects a blank identifier. Keeping the column non-null
/// also keeps the unique index free of NULLs, which matters because SQL Server and PostgreSQL
/// disagree about whether NULLs are distinct in a unique index.
/// </para>
/// </remarks>
public static class SubscriptionCheckpointTenantColumn
{
	/// <summary>The value stored for <see cref="TenantScope.System"/>.</summary>
	public const string SystemValue = "";

	/// <summary>Encodes a scope for storage.</summary>
	/// <param name="scope">The scope to encode.</param>
	public static string Encode(TenantScope scope) => scope.IsSystem ? SystemValue : scope.TenantId!;
}
