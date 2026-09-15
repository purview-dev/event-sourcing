using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Distributed;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Snapshotting;

namespace Purview.EventSourcing;

/// <summary>
/// Options to influence Event Store operations.
/// </summary>
[System.Diagnostics.DebuggerStepThrough]
public sealed record class EventStoreOperationContext
{
	readonly ConcurrentDictionary<Type, object> _snapshotStrategies = new();

	/// <summary>
	/// Get or sets the default <see cref="EventStoreOperationContext"/>, for
	/// when <see cref="IEventStore{T}"/> operations provide a null operational context.
	/// </summary>
	public static EventStoreOperationContext DefaultContext(string? correlationId = null) =>
		new() { CorrelationId = correlationId! };

	/// <summary>
	/// Gets/ sets the default value for <see cref="RequiresValidPrincipalIdentifier"/>.
	/// </summary>
	public static bool RequiresValidPrincipalIdentifierDefault { get; set; } = true;

	/// <summary>
	/// Gets or sets the default value for the <see cref="UseIdempotencyMarker"/>. Defaults to false.
	/// </summary>
	public static bool UseIdempotencyMarkerDefault { get; set; }

	/// <summary>
	/// Gets or sets the default value for the <see cref="UseSnapshotCache"/>. Defaults to true.
	/// </summary>
	public static SnapshotCachingOptions UseDefaultCacheMode { get; set; } = SnapshotCachingOptions.GetAndStore;

	/// <summary>
	/// Gets or sets the default value for <see cref="RequireSnapshotWrite"/>. Defaults to true.
	/// </summary>
	public static bool RequireSnapshotWriteDefault { get; set; } = true;

	/// <summary>
	/// Gets or sets a value indicating whether a snapshot write that fails must fail the save.
	/// When true (the default), the snapshot is written atomically with the events in the same
	/// batch/transaction. When false, the snapshot is written best-effort after the events are
	/// committed and a snapshot failure is logged rather than failing the save.
	/// </summary>
	public bool RequireSnapshotWrite { get; set; } = RequireSnapshotWriteDefault;

	/// <summary>
	/// Used during the saving of aggregates. The value is resolved once and cached for the lifetime
	/// of this context, so a single save operation shares one correlation id across all of its events.
	/// </summary>
	public string CorrelationId
	{
		get => field ??= System.Diagnostics.Activity.Current?.Id ?? $"{Guid.NewGuid()}";
		set;
	}

	/// <summary>
	/// Controls how to handle aggregates marked as deleted during get and save/ restore operations.
	/// </summary>
	public DeleteHandlingMode DeleteMode { get; set; } = DeleteHandlingMode.ReturnsNull;

	/// <summary>
	/// Controls how to handle aggregates marked as locked.
	/// </summary>
	public LockHandlingMode LockMode { get; set; } = LockHandlingMode.ThrowsException;

	/// <summary>
	/// Gets/ sets a value indicating how the <see cref="IEventStore{T}"/>
	/// uses the <see cref="IDistributedCache"/> during it's operations. Defaults to <see cref="SnapshotCachingOptions.GetAndStore"/>.
	/// </summary>
	public SnapshotCachingOptions SnapshotCacheMode { get; set; } = SnapshotCachingOptions.GetAndStore;

	/// <summary>
	/// When true, a cached aggregate is validated against the aggregate's persisted stream version
	/// before it is returned, so a stale cache entry is never served.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Defaults to <see langword="false"/> because the check adds a storage read per cache hit.
	/// The distributed cache is normally kept consistent by writing it after a successful commit and
	/// invalidating on failure and permanent deletes; enable this only when cache invalidation cannot be
	/// guaranteed (for example a replicated cache with lag across regions).
	/// </para>
	/// </remarks>
	public bool ValidateCachedSnapshot { get; set; }

	/// <summary>
	/// Manages caching operations for the operation.
	/// </summary>
	public DistributedCacheEntryOptions? CacheOptions { get; set; }

	/// <summary>
	/// <para>If true, the event store will skip getting the snapshot and instead attempt to reconstitute the aggregate entirely from it's events.</para>
	/// <para>Otherwise, if the <see cref="IEventStore{T}"/> supports snapshots then a pre-reconstituted snapshot may be retrieved.</para>
	/// </summary>
	/// <remarks>This can be a slow operation.</remarks>
	public bool SkipSnapshot { get; set; }

	/// <summary>
	/// Controls the notification mode when save, delete or restore operations occur.
	/// </summary>
	public NotificationModes NotificationMode { get; set; } = NotificationModes.All;

	/// <summary>
	/// <para>
	/// When false, aggregates are soft deleted. A platform event is added to mark the aggregate as deleted,
	/// and further save operations are forbidden. You can still get the aggregate by using the <see cref="DeleteMode"/> property
	/// and the <see cref="IEventStore{T}.RestoreAsync(T, EventStoreOperationContext?, CancellationToken)"/> operation.
	/// </para>
	/// <para>WARNING: If true, the aggregate and all of it's events and additional information are permanently deleted.</para>
	/// </summary>
	public bool PermanentlyDelete { get; set; }

	/// <summary>
	/// Validates the idempotency marker to see if the events have already been written.
	/// </summary>
	/// <remarks><para>If the marker exists, the call to <see cref="IEventStore{T}.SaveAsync(T, EventStoreOperationContext?, CancellationToken)"/> will return true without processing any of the events.</para>
	/// <para>In the case where this property is set to false, and the idempotency marker exists and internal exception is caught and ignored, and the operation requested will still set to true.</para>
	/// <para>If you're satisfied that the call is unique, then you can leave this as false (the default) for the sake of performance, otherwise set this to true.</para></remarks>
	public bool UseIdempotencyMarker { get; set; } = UseIdempotencyMarkerDefault;

	/// <summary>
	/// If true, a valid <see cref="ClaimIdentifier"/> must be returned from <see cref="ClaimsPrincipal"/> or
	/// an exception is thrown.
	/// </summary>
	public bool RequiresValidPrincipalIdentifier { get; set; } = RequiresValidPrincipalIdentifierDefault;

	/// <summary>
	/// The claim identifier to use when retrieving the Id from the <see cref="ClaimsPrincipal"/>.
	/// </summary>
	public string ClaimIdentifier { get; set; } = "sub";

	/// <summary>
	/// Gets or sets a selector that can resolve snapshot strategies for this specific operation.
	/// </summary>
	public ISnapshotStrategySelector? SnapshotStrategySelector { get; set; }

	/// <summary>
	/// Sets the snapshot strategy to use for <typeparamref name="TAggregate"/> for this operation.
	/// </summary>
	public EventStoreOperationContext SetSnapshotStrategy<TAggregate>(ISnapshotStrategy<TAggregate> strategy)
		where TAggregate : class, IAggregate, new()
	{
		ArgumentNullException.ThrowIfNull(strategy);
		_snapshotStrategies.AddOrUpdate(typeof(TAggregate), strategy, (key, oldValue) => strategy);
		return this;
	}

	/// <summary>
	/// Attempts to resolve an operation-specific snapshot strategy for <typeparamref name="TAggregate"/>.
	/// </summary>
	public bool TryGetSnapshotStrategy<TAggregate>(out ISnapshotStrategy<TAggregate>? strategy)
		where TAggregate : class, IAggregate, new()
	{
		if (_snapshotStrategies.TryGetValue(typeof(TAggregate), out var value))
		{
			strategy = value as ISnapshotStrategy<TAggregate>;
			return strategy is not null;
		}

		strategy = null;
		return false;
	}
}
