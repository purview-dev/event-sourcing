using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing;

/// <summary>
/// Convenience extension methods over <see cref="IEventStoreCore{T}"/> for common create, get, save,
/// delete, and restore workflows.
/// </summary>
/// <remarks>
/// These helpers reduce boilerplate by supplying default operation contexts, converting object-based ids to
/// strings, and running creator callbacks on newly created aggregates. They are hidden from IntelliSense as
/// they are intended for use through the main <see cref="IEventStore"/> and <see cref="IQueryableEventStore"/>
/// facades.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
[System.Diagnostics.DebuggerStepThrough]
public static class IEventStoreCoreExtensions
{
	/// <summary>
	/// Gets the aggregate for the id, creating and initializing it with the <paramref name="creator"/> when it
	/// does not yet exist.
	/// </summary>
	/// <typeparam name="T">The <see cref="IAggregate"/> type.</typeparam>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> used as the root object.</param>
	/// <param name="aggregateId">The id of the aggregate to get, or use as the id of the aggregate to create.</param>
	/// <param name="creator">The asynchronous callback used to initialize a newly created aggregate.</param>
	/// <param name="context">The operational context controlling how the aggregate is retrieved.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The existing or newly created aggregate, or the reason it could not be read.</returns>
	public static async Task<Result<T, AggregateReadFailure>> GetOrCreateAsync<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		string? aggregateId,
		[NotNull] Func<T, CancellationToken, Task> creator,
		EventStoreOperationContext? context,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new()
	{
		var result = await eventStore.GetOrCreateAsync(aggregateId, context, cancellationToken);
		if (result.IsFailure)
			return result;

		var aggregate = result.Value;
		if (aggregate.IsNew())
			await creator(aggregate, cancellationToken);

		return aggregate;
	}

	/// <summary>
	/// Gets the aggregate for the id, creating it when it does not yet exist using the default context.
	/// </summary>
	/// <typeparam name="T">The <see cref="IAggregate"/> type.</typeparam>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> used as the root object.</param>
	/// <param name="aggregateId">The id of the aggregate to get, or use as the id of the aggregate to create.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The existing or newly created aggregate, or the reason it could not be read.</returns>
	public static Task<Result<T, AggregateReadFailure>> GetOrCreateAsync<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		string? aggregateId,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() => eventStore.GetOrCreateAsync(aggregateId, null, cancellationToken);

	/// <summary>
	/// Gets the aggregate for the id, creating and initializing it with the <paramref name="creator"/> when it
	/// does not yet exist using the default context.
	/// </summary>
	/// <typeparam name="T">The <see cref="IAggregate"/> type.</typeparam>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> used as the root object.</param>
	/// <param name="aggregateId">The id of the aggregate to get, or use as the id of the aggregate to create.</param>
	/// <param name="creator">The asynchronous callback used to initialize a newly created aggregate.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The existing or newly created aggregate, or the reason it could not be read.</returns>
	public static async Task<Result<T, AggregateReadFailure>> GetOrCreateAsync<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		string? aggregateId,
		[NotNull] Func<T, CancellationToken, Task> creator,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new()
	{
		var result = await eventStore.GetOrCreateAsync(aggregateId, null, cancellationToken);
		if (result.IsFailure)
			return result;

		var aggregate = result.Value;
		if (aggregate.IsNew())
			await creator(aggregate, cancellationToken);

		return aggregate;
	}

	/// <summary>
	/// Creates a new aggregate of <typeparamref name="T"/> and optionally runs the <paramref name="creator"/>
	/// callback against it.
	/// </summary>
	/// <typeparam name="T">The <see cref="IAggregate"/> to create.</typeparam>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> used as the root object.</param>
	/// <param name="aggregateId">Optional, the id of the aggregate to create; when null, an id is generated.</param>
	/// <param name="creator">Optional asynchronous callback used to initialize the new aggregate.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The newly created aggregate.</returns>
	public static async Task<T> CreateAsync<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		string? aggregateId = null,
		Func<T, CancellationToken, Task>? creator = null,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new()
	{
		var aggregate = await eventStore.CreateAsync(aggregateId, cancellationToken);
		if (creator != null)
			await creator(aggregate, cancellationToken);

		return aggregate;
	}

	/// <summary>
	/// Gets the aggregate for the id using the default operation context.
	/// </summary>
	/// <typeparam name="T">The <see cref="IAggregate"/> type.</typeparam>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> used as the root object.</param>
	/// <param name="aggregateId">The id of the aggregate to get.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The requested aggregate, or the reason it could not be read.</returns>
	public static Task<Result<T, AggregateReadFailure>> GetAsync<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		string aggregateId,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() => eventStore.GetAsync(aggregateId, null, cancellationToken);

	/// <summary>
	/// Gets the aggregate up to a specific version using the default operation context.
	/// </summary>
	/// <typeparam name="T">The <see cref="IAggregate"/> type.</typeparam>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> used as the root object.</param>
	/// <param name="aggregateId">The id of the aggregate to get.</param>
	/// <param name="version">The version of the aggregate to get.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The requested aggregate, or the reason it could not be read.</returns>
	public static Task<Result<T, AggregateReadFailure>> GetAtAsync<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		string aggregateId,
		long version,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() => eventStore.GetAtAsync(aggregateId, version, null, cancellationToken);

	/// <summary>
	/// Creates a new <see cref="IEventStoreTransaction"/> and enlists all <paramref name="aggregates"/>
	/// against this event store, using an auto-generated correlation ID.
	/// </summary>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> responsible for persisting each aggregate.</param>
	/// <param name="aggregates">The aggregates to include in the transaction.</param>
	/// <returns>A new <see cref="IEventStoreTransaction"/> ready to be committed.</returns>
	/// <remarks>Call <see cref="IEventStoreTransaction.CommitAsync"/> to persist all enlisted aggregates.</remarks>
	public static IEventStoreTransaction Enlist<T>([NotNull] this IEventStoreCore<T> eventStore, params T[] aggregates)
		where T : class, IAggregate, new() => eventStore.Enlist(correlationId: null, aggregates);

	/// <summary>
	/// Creates a new <see cref="IEventStoreTransaction"/> with the given <paramref name="correlationId"/>
	/// and enlists all <paramref name="aggregates"/> against this event store.
	/// </summary>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> responsible for persisting each aggregate.</param>
	/// <param name="correlationId">
	/// Optional correlation ID to bind all events together. When <see langword="null"/>, a new GUID is generated.
	/// </param>
	/// <param name="aggregates">The aggregates to include in the transaction.</param>
	/// <returns>A new <see cref="IEventStoreTransaction"/> ready to be committed.</returns>
	public static IEventStoreTransaction Enlist<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		string? correlationId,
		params T[] aggregates
	)
		where T : class, IAggregate, new()
	{
		ArgumentNullException.ThrowIfNull(eventStore);
		ArgumentNullException.ThrowIfNull(aggregates);

		EventStoreTransaction transaction = new(correlationId);
		foreach (var aggregate in aggregates)
			transaction.Enlist(aggregate, eventStore);

		return transaction;
	}

	/// <summary>
	/// Creates a new <see cref="IEventStoreTransaction"/> and enlists all <paramref name="aggregates"/>
	/// against this event store, applying a shared <paramref name="operationContext"/> to each.
	/// </summary>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> responsible for persisting each aggregate.</param>
	/// <param name="operationContext">
	/// Optional <see cref="EventStoreOperationContext"/> applied to every aggregate save.
	/// When <see langword="null"/>, the default context is used.
	/// </param>
	/// <param name="aggregates">The aggregates to include in the transaction.</param>
	/// <returns>A new <see cref="IEventStoreTransaction"/> ready to be committed.</returns>
	public static IEventStoreTransaction Enlist<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		EventStoreOperationContext? operationContext,
		params T[] aggregates
	)
		where T : class, IAggregate, new()
	{
		ArgumentNullException.ThrowIfNull(eventStore);
		ArgumentNullException.ThrowIfNull(aggregates);

		EventStoreTransaction transaction = new(operationContext?.CorrelationId);
		foreach (var aggregate in aggregates)
			transaction.Enlist(aggregate, eventStore, operationContext);

		return transaction;
	}

	/// <summary>
	/// Saves the aggregate using the default operation context.
	/// </summary>
	/// <typeparam name="T">The <see cref="IAggregate"/> type.</typeparam>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> used as the root object.</param>
	/// <param name="aggregate">The aggregate to save.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The outcome of the save, or the reason it did not happen.</returns>
	public static Task<Result<SaveOutcome<T>, AggregateSaveFailure>> SaveAsync<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		T aggregate,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() => eventStore.SaveAsync(aggregate, null, cancellationToken);

	/// <summary>
	/// Deletes the aggregate using the default operation context.
	/// </summary>
	/// <typeparam name="T">The <see cref="IAggregate"/> type.</typeparam>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> used as the root object.</param>
	/// <param name="aggregate">The aggregate to delete.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The outcome of the delete, or the reason it did not happen.</returns>
	public static Task<Result<SaveOutcome<T>, AggregateDeleteFailure>> DeleteAsync<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		T aggregate,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() => eventStore.DeleteAsync(aggregate, null, cancellationToken);

	/// <summary>
	/// Deletes the aggregate with the given id using the default operation context.
	/// </summary>
	/// <typeparam name="T">The <see cref="IAggregate"/> type.</typeparam>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> used as the root object.</param>
	/// <param name="aggregateId">The id of the aggregate to delete.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The outcome of the delete, or the reason it did not happen.</returns>
	public static Task<Result<SaveOutcome<T>, AggregateDeleteFailure>> DeleteAsync<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		string aggregateId,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() => eventStore.DeleteAsync(aggregateId, null, cancellationToken);

	/// <summary>
	/// Deletes the aggregate with the given id using the supplied operation context.
	/// </summary>
	/// <typeparam name="T">The <see cref="IAggregate"/> type.</typeparam>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> used as the root object.</param>
	/// <param name="aggregateId">The id of the aggregate to delete.</param>
	/// <param name="operationContext">The operational context controlling how the aggregate is deleted.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The outcome of the delete, or the reason it did not happen.</returns>
	/// <exception cref="ArgumentException">Thrown when <paramref name="aggregateId"/> is null or whitespace.</exception>
	public static async Task<Result<SaveOutcome<T>, AggregateDeleteFailure>> DeleteAsync<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId, nameof(aggregateId));

		var result = await eventStore.GetAsync(aggregateId, operationContext, cancellationToken);

		return result.IsFailure
			? ToDeleteFailure<T>(result.Error, aggregateId)
			: await eventStore.DeleteAsync(result.Value, operationContext, cancellationToken);
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

	/// <summary>
	/// Restores a previously deleted aggregate using the default operation context.
	/// </summary>
	/// <typeparam name="T">The <see cref="IAggregate"/> type.</typeparam>
	/// <param name="eventStore">The <see cref="IEventStoreCore{T}"/> used as the root object.</param>
	/// <param name="aggregate">The aggregate to restore.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The outcome of the restore, or the reason it did not happen.</returns>
	public static Task<Result<SaveOutcome<T>, AggregateRestoreFailure>> RestoreAsync<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		T aggregate,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new() => eventStore.RestoreAsync(aggregate, null, cancellationToken);
}
