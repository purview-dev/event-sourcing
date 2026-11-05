namespace Purview.EventSourcing.Exceptions;

/// <summary>
/// Thrown when an optimistic-concurrency violation is detected while saving events.
/// </summary>
/// <param name="aggregateId">The id of the aggregate.</param>
/// <param name="idempotencyId">The idempotency identifier of the save operation.</param>
/// <param name="versionAttempted">The aggregate version that was attempted.</param>
/// <param name="version">The version currently present in the store.</param>
/// <remarks>
/// One type for every provider: a caller catches this without referencing a provider assembly, and
/// <see cref="ConcurrencyRetry"/> recognizes it through <see cref="IConcurrencyConflict"/>.
/// </remarks>
#pragma warning disable CA1032 // Implement standard exception constructors
public class ConcurrencyException(string aggregateId, string idempotencyId, long versionAttempted, long version)
	: Exception(
		$"Optimistic concurrency error:\n\tAggregateId: {aggregateId}\n\tIdempotencyId: {idempotencyId}\n\tVersionAttempted:{versionAttempted}\n\tVersionPresent:{version}"
	),
		IConcurrencyConflict
#pragma warning restore CA1032 // Implement standard exception constructors
{
	/// <summary>
	/// The id of the aggregate that failed the concurrency check.
	/// </summary>
	public string AggregateId { get; } = aggregateId ?? throw new ArgumentNullException(nameof(aggregateId));

	/// <summary>
	/// The aggregate version that was attempted.
	/// </summary>
	public long VersionAttempted { get; } = versionAttempted;

	/// <summary>
	/// The idempotency identifier of the save operation.
	/// </summary>
	public string IdempotencyId { get; } = idempotencyId ?? throw new ArgumentNullException(nameof(idempotencyId));

	/// <summary>
	/// The version currently present in the store.
	/// </summary>
	public long Version { get; } = version;
}
