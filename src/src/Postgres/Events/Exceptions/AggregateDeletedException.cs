#pragma warning disable CA1032 // Implement standard exception constructors

namespace Purview.EventSourcing.Postgres.Events.Exceptions;

/// <summary>
/// Thrown when an attempt is made to save an aggregate that has been deleted.
/// </summary>
/// <param name="aggregateId">The id of the deleted aggregate.</param>
/// <param name="idempotencyId">The idempotency id of the operation that attempted the save.</param>
public class AggregateDeletedException(string aggregateId, string idempotencyId)
	: Exception(
		$"An attempt to save an aggregate that has been deleted, aggregate Id: {aggregateId}, {nameof(IdempotencyId)}: {idempotencyId}."
	)
{
	/// <summary>
	/// The id of the deleted aggregate.
	/// </summary>
	public string AggregateId { get; } = aggregateId ?? throw new ArgumentNullException(nameof(aggregateId));

	/// <summary>
	/// The idempotency id of the operation that attempted the save.
	/// </summary>
	public string IdempotencyId { get; } = idempotencyId ?? throw new ArgumentNullException(nameof(idempotencyId));
}
