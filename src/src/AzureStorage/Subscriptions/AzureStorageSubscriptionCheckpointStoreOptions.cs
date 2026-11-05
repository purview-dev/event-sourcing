using System.ComponentModel.DataAnnotations;

namespace Purview.EventSourcing.AzureStorage.Subscriptions;

/// <summary>
/// Options that configure the Azure Table Storage subscription checkpoint store.
/// </summary>
/// <remarks>
/// Bound from the <c>EventStore:AzureStorage:Subscriptions</c> configuration section. When
/// <see cref="ConnectionString"/> is not supplied, the event-store connection string is used.
/// </remarks>
public sealed class AzureStorageSubscriptionCheckpointStoreOptions
{
	/// <summary>The configuration section name used to bind these options.</summary>
	public const string AzureStorageSubscriptions = "EventStore:AzureStorage:Subscriptions";

	/// <summary>The default table name used to persist subscription positions.</summary>
	public const string DefaultTableName = "EventStoreSubscriptionCheckpoints";

	/// <summary>
	/// The storage connection string. When null, the event-store connection string is used.
	/// </summary>
	public string? ConnectionString { get; set; }

	/// <summary>The name of the table that stores subscription positions.</summary>
	/// <remarks>Azure table names must be alphanumeric and start with a letter.</remarks>
	[Required(AllowEmptyStrings = false)]
	[RegularExpression("^[A-Za-z][A-Za-z0-9]{2,62}$")]
	public string TableName { get; set; } = DefaultTableName;

	/// <summary>
	/// How many times a contended position write is retried before it is left for the next cycle.
	/// </summary>
	/// <remarks>
	/// Table Storage cannot express "update only if the stored version is lower", so the guard is a
	/// read followed by an ETag-conditional write. Two instances running the same subscription can
	/// collide; giving up leaves the position behind, which redelivers rather than skips.
	/// </remarks>
	[Range(1, 20)]
	public int MaxWriteAttempts { get; set; } = 5;
}
