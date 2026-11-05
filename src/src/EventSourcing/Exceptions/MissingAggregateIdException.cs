namespace Purview.EventSourcing.Exceptions;

/// <summary>
/// Thrown when a save is attempted for an aggregate that has no id.
/// </summary>
/// <param name="idempotencyId">The idempotency identifier of the save operation.</param>
#pragma warning disable CA1032 // Implement standard exception constructors
public class MissingAggregateIdException(string idempotencyId)
	: Exception($"An attempt to save an aggregate with a missing Id was made, IdempotencyId: {idempotencyId}.")
#pragma warning restore CA1032 // Implement standard exception constructors
{
	/// <summary>
	/// The idempotency identifier of the save operation.
	/// </summary>
	public string IdempotencyId { get; } = idempotencyId ?? throw new ArgumentNullException(nameof(idempotencyId));
}
