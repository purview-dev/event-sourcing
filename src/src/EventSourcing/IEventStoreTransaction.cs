using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing;

/// <summary>
/// Represents a logical transaction that coordinates saving one or more aggregates atomically.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="Enlist{T}(T, IEventStore, EventStoreOperationContext?)"/> or
/// <see cref="Enlist{T}(T, IEventStoreCore{T}, EventStoreOperationContext?)"/> to register aggregates that should be saved together.
/// Call <see cref="CommitAsync"/> to persist all enlisted aggregates in a single logical operation.
/// </para>
/// <para>
/// Disposing without calling <see cref="CommitAsync"/> discards all changes — the aggregates'
/// unsaved events remain, but nothing is written to the store.
/// </para>
/// <para>
/// The concrete implementation determines the atomicity guarantees:
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="EventStoreTransaction"/> — coordinates multiple <see cref="IEventStore{T}.SaveAsync"/>
///     calls under a shared correlation ID. If one save fails the remaining aggregates are <em>not</em>
///     persisted, but those already written are <em>not</em> rolled back (eventual-consistency model).
///   </item>
///   <item>
///     Store-specific implementations (e.g. SQL Server) may provide true ACID guarantees when all
///     aggregates share the same backing store.
///   </item>
/// </list>
/// </remarks>
public interface IEventStoreTransaction : IAsyncDisposable
{
	/// <summary>
	/// Gets the minimum persistence guarantee requested by the caller.
	/// </summary>
	EventStoreTransactionGuarantee RequiredGuarantee => EventStoreTransactionGuarantee.BestEffort;

	/// <summary>
	/// Gets the guarantee the transaction can provide for the stores currently enlisted.
	/// </summary>
	/// <remarks>
	/// Empty and single-store transactions report <see cref="EventStoreTransactionGuarantee.Atomic"/> only when
	/// their store exposes a native transaction boundary. The value can change as stores are enlisted.
	/// </remarks>
	EventStoreTransactionGuarantee AvailableGuarantee => EventStoreTransactionGuarantee.BestEffort;

	/// <summary>
	/// The correlation ID that binds all aggregates in this transaction together.
	/// </summary>
	/// <remarks>
	/// This value is automatically propagated to every <see cref="Aggregates.Events.EventMetadata.CorrelationId"/>
	/// when <see cref="CommitAsync"/> is called.
	/// </remarks>
	string CorrelationId { get; }

	/// <summary>
	/// Registers an aggregate for inclusion in this transaction.
	/// </summary>
	/// <typeparam name="T">The aggregate type.</typeparam>
	/// <param name="aggregate">The aggregate with unsaved events to include in the commit.</param>
	/// <param name="eventStore">The <see cref="IEventStore"/> responsible for persisting <paramref name="aggregate"/>.</param>
	/// <param name="operationContext">
	/// Optional <see cref="EventStoreOperationContext"/>. When <see langword="null"/>,
	/// the default context is used with this transaction's <see cref="CorrelationId"/>.
	/// </param>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="CommitAsync"/> has already been called.
	/// </exception>
	void Enlist<T>(T aggregate, IEventStore eventStore, EventStoreOperationContext? operationContext = null)
		where T : class, IAggregate, new();

	/// <summary>
	/// Registers an aggregate for inclusion in this transaction using a typed implementation contract.
	/// </summary>
	/// <typeparam name="T">The aggregate type.</typeparam>
	/// <param name="aggregate">The aggregate with unsaved events to include in the commit.</param>
	/// <param name="eventStore">The typed implementation responsible for persisting <paramref name="aggregate"/>.</param>
	/// <param name="operationContext">
	/// Optional <see cref="EventStoreOperationContext"/>. When <see langword="null"/>,
	/// the default context is used with this transaction's <see cref="CorrelationId"/>.
	/// </param>
	void Enlist<T>(T aggregate, IEventStoreCore<T> eventStore, EventStoreOperationContext? operationContext = null)
		where T : class, IAggregate, new();

	/// <summary>
	/// Persists all enlisted aggregates.
	/// </summary>
	/// <param name="cancellationToken">The stopping token.</param>
	/// <returns>
	/// A <see cref="TransactionResult"/> summarising the outcome of each aggregate save.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="CommitAsync"/> has already been called.
	/// </exception>
	/// <exception cref="EventStoreTransactionGuaranteeException">
	/// Thrown before any save is attempted when <see cref="RequiredGuarantee"/> is unavailable.
	/// </exception>
	Task<TransactionResult> CommitAsync(CancellationToken cancellationToken = default);
}
