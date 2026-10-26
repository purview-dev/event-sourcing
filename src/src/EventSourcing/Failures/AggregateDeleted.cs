namespace Purview.EventSourcing.Failures;

/// <summary>
/// The stream exists but the aggregate is soft-deleted.
/// </summary>
/// <param name="AggregateType">The aggregate type name.</param>
/// <param name="AggregateId">The identifier of the deleted aggregate.</param>
/// <remarks>
/// A deleted aggregate can be read with <see cref="EventStoreOperationContext.DeleteMode"/> and brought back
/// with <see cref="IEventStore{T}.RestoreAsync(T, EventStoreOperationContext?, System.Threading.CancellationToken)"/>.
/// </remarks>
public readonly record struct AggregateDeleted(string AggregateType, string AggregateId);
