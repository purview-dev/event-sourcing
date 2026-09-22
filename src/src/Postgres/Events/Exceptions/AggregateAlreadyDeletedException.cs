#pragma warning disable CA1032 // Implement standard exception constructors

namespace Purview.EventSourcing.Postgres.Events.Exceptions;

/// <summary>
/// Thrown when an attempt is made to delete an aggregate that has already been deleted.
/// </summary>
/// <param name="aggregateId">The id of the aggregate that is already deleted.</param>
/// <param name="idempotencyId">The idempotency id of the operation that attempted the deletion.</param>
public class AggregateAlreadyDeletedException(string aggregateId, string idempotencyId)
	: Exception(
		$"An attempt to delete an aggregate (Id: {aggregateId}) that is already deleted was made. {nameof(idempotencyId)}: {idempotencyId}."
	)
{
	/// <summary>
	/// The id of the aggregate that is already deleted.
	/// </summary>
	public string AggregateId { get; } = aggregateId ?? throw new ArgumentNullException(nameof(aggregateId));

	/// <summary>
	/// The idempotency id of the operation that attempted the deletion.
	/// </summary>
	public string IdempotencyId { get; } = idempotencyId ?? throw new ArgumentNullException(nameof(idempotencyId));
}
