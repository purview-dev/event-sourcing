using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing;

/// <summary>
/// Convenience extension methods over <see cref="IEventStore"/> for common create, get, save, delete,
/// restore, and transaction workflows.
/// </summary>
/// <remarks>
/// These helpers reduce boilerplate by supplying default operation contexts, converting object-based ids to
/// strings, running creator callbacks on newly created aggregates, and offering shorter transaction
/// enlistment forms. They are hidden from IntelliSense as they are intended for use through the main
/// <see cref="IEventStore"/> and <see cref="IQueryableEventStore"/> facades.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
[System.Diagnostics.DebuggerStepThrough]
public static partial class IEventStoreExtensions
{
	/// <summary>
	/// Creates a new <see cref="IEventStoreTransaction"/> with no enlisted aggregates.
	/// </summary>
	/// <param name="eventStore">The <see cref="IEventStore"/> used as the root object.</param>
	/// <returns>A new <see cref="IEventStoreTransaction"/> ready to be committed.</returns>
	public static IEventStoreTransaction Enlist([NotNull] this IEventStore eventStore)
	{
		ArgumentNullException.ThrowIfNull(eventStore);

		return new EventStoreTransaction();
	}

	/// <summary>
	/// Creates a new <see cref="IEventStoreTransaction"/> with the given <paramref name="correlationId"/>
	/// and no enlisted aggregates.
	/// </summary>
	/// <param name="eventStore">The <see cref="IEventStore"/> used as the root object.</param>
	/// <param name="correlationId">
	/// Optional correlation ID to bind all events together. When <see langword="null"/>, a new GUID is generated.
	/// </param>
	/// <returns>A new <see cref="IEventStoreTransaction"/> ready to be committed.</returns>
	public static IEventStoreTransaction Enlist([NotNull] this IEventStore eventStore, string? correlationId)
	{
		ArgumentNullException.ThrowIfNull(eventStore);

		return new EventStoreTransaction(correlationId);
	}

	/// <summary>
	/// Creates a new <see cref="IEventStoreTransaction"/> with the given <paramref name="operationContext"/>
	/// and no enlisted aggregates.
	/// </summary>
	/// <param name="eventStore">The <see cref="IEventStore"/> used as the root object.</param>
	/// <param name="operationContext">
	/// Optional <see cref="EventStoreOperationContext"/> applied to every aggregate save.
	/// When <see langword="null"/>, the default context is used.
	/// </param>
	/// <returns>A new <see cref="IEventStoreTransaction"/> ready to be committed.</returns>
	public static IEventStoreTransaction Enlist(
		[NotNull] this IEventStore eventStore,
		EventStoreOperationContext? operationContext
	)
	{
		ArgumentNullException.ThrowIfNull(eventStore);

		return new EventStoreTransaction(operationContext?.CorrelationId);
	}

	// A read that fails on the way to a delete is still a delete failure, so the read case is widened
	// rather than reported as "not found" for every reason the read did not produce an aggregate.
	static Result<SaveOutcome<T>, AggregateDeleteFailure> ToDeleteFailure<T>(
		AggregateReadFailure failure,
		string aggregateId
	)
		where T : class, IAggregate, new() =>
		failure switch
		{
			AggregateDeleted deleted => AggregateDeleteFailure.Failure<SaveOutcome<T>>(
				new AggregateAlreadyDeleted(deleted.AggregateType, deleted.AggregateId)
			),
			AggregateLocked locked => AggregateDeleteFailure.Failure<SaveOutcome<T>>(locked),
			AggregateNotFound notFound => AggregateDeleteFailure.Failure<SaveOutcome<T>>(notFound),
			// GetAsync never reports AggregateNotDeleted; it belongs to GetDeletedAsync.
			_ => AggregateDeleteFailure.Failure<SaveOutcome<T>>(new AggregateNotFound(typeof(T).Name, aggregateId)),
		};
}
