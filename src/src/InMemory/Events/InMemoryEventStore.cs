using System.Collections.Concurrent;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.Services;
using Purview.EventSourcing.Tenancy;
using Purview.EventSourcing.Validation;
using Purview.Results;

namespace Purview.EventSourcing.InMemory.Events;

/// <summary>
/// An in-memory <see cref="IEventStoreCore{T}"/> that stores aggregates and their events in process,
/// without queryable snapshot reads.
/// </summary>
/// <typeparam name="T">An <see cref="IAggregate"/> implementation.</typeparam>
/// <param name="aggregateChangeNotifier">The notifier invoked before and after aggregates are saved or deleted.</param>
/// <param name="aggregateRequirementsManager">The manager used to fulfil aggregate requirements.</param>
/// <param name="validator">Optional <see cref="IAggregateValidator{T}"/> used to validate aggregates before they are saved.</param>
/// <param name="aggregateIdFactory">Optional factory used to generate aggregate ids when none is supplied.</param>
/// <param name="principalAccessor">Optional accessor supplying the principal recorded against saved events.</param>
/// <param name="metrics">Optional metrics recorder; defaults to a no-op.</param>
/// <param name="currentTenant">Optional tenant scope. This store does not enforce tenant isolation and refuses a tenant-scoped operation.</param>
/// <remarks>
/// Aggregates are held in memory and are identified by id in a case-insensitive manner. This store is
/// intended for testing and single-process scenarios; data is not shared between instances or persisted
/// across restarts.
/// </remarks>
/// <seealso cref="IInMemoryEventStore{T}"/>
public partial class InMemoryEventStore<T>(
	ChangeFeed.IAggregateChangeFeedNotifier<T> aggregateChangeNotifier,
	IAggregateRequirementsManager aggregateRequirementsManager,
	IAggregateValidator<T>? validator = null,
	IAggregateIdFactory? aggregateIdFactory = null,
	IEventStorePrincipalAccessor? principalAccessor = null,
	IEventStoreMetrics? metrics = null,
	ICurrentTenant? currentTenant = null
) : IInMemoryEventStore<T>, IDisposable
	where T : class, IAggregate, new()
{
	readonly ConcurrentDictionary<string, T> _aggregates = new(StringComparer.OrdinalIgnoreCase);
	readonly ConcurrentDictionary<string, ConcurrentDictionary<long, EventRecord>> _events = new();

	readonly IAggregateValidator<T>? _validator = validator;

	readonly IEventStorePrincipalAccessor _principalAccessor =
		principalAccessor ?? new AmbientEventStorePrincipalAccessor();

	readonly IEventStoreMetrics _metrics = metrics ?? NoOpEventStoreMetrics.Instance;

	readonly ICurrentTenant _currentTenant = currentTenant ?? new SystemCurrentTenant();

	// The tenant that owns each stream. Kept beside the data rather than folded into the key so the
	// system scope can still enumerate every tenant, exactly as the SQL providers allow.
	readonly ConcurrentDictionary<string, string?> _tenants = new(StringComparer.OrdinalIgnoreCase);

	// Reads fail closed: an unowned stream is visible only to the system scope. Treating it as visible
	// to every tenant would turn a missed stamp into silent cross-tenant exposure.
	bool IsVisible(string aggregateId)
	{
		var scope = _currentTenant.Current;

		return scope.IsSystem
			|| (
				_tenants.TryGetValue(aggregateId, out var owner)
				&& string.Equals(owner, scope.TenantId, StringComparison.OrdinalIgnoreCase)
			);
	}

	// Writes are the one place an unowned stream must be allowed: that is a stream being created, which
	// has no owner until this save records one.
	bool CanWrite(string aggregateId) => !_tenants.ContainsKey(aggregateId) || IsVisible(aggregateId);

	/// <summary>
	/// The aggregates currently held by the store.
	/// </summary>
	/// <remarks>Exposed so derived snapshot stores can enumerate the in-memory aggregates.</remarks>
	protected IEnumerable<T> Aggregates => _aggregates.Values;

	///<inheritdoc/>
	public Task ClearAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		_aggregates.Clear();
		_events.Clear();

		return Task.CompletedTask;
	}

	static bool ReturnAggregate(bool isDeleted, EventStoreOperationContext context) =>
		!isDeleted || context.DeleteMode == DeleteHandlingMode.ReturnsAggregate;

	///<inheritdoc/>
	public async Task<T> CreateAsync(string? aggregateId = null, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(aggregateId))
		{
			if (aggregateIdFactory != null)
			{
				aggregateId = await aggregateIdFactory.CreateAsync<T>(cancellationToken);
				if (string.IsNullOrWhiteSpace(aggregateId))
					throw new NullReferenceException(
						$"The {typeof(IAggregateIdFactory).FullName} implementation ({aggregateIdFactory.GetType().FullName}) generated a null or empty Id."
					);
			}
			else
				aggregateId = $"{Guid.NewGuid()}:D";
		}

		T aggregate = new() { Details = { Id = aggregateId } };

		return FulfilRequirements(aggregate);
	}

	///<inheritdoc/>
	public async Task<Result<SaveOutcome<T>, AggregateDeleteFailure>> DeleteAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		if (aggregate == null)
			throw NullAggregate(aggregate);

		if (aggregate.Details.IsDeleted)
		{
			return AggregateDeleteFailure.Failure<SaveOutcome<T>>(
				new AggregateAlreadyDeleted(aggregate.AggregateType, aggregate.Id())
			);
		}

		operationContext ??= EventStoreOperationContext.DefaultContext();

		if (aggregate.IsNew())
		{
			return AggregateDeleteFailure.Failure<SaveOutcome<T>>(
				new AggregateNotFound(aggregate.AggregateType, aggregate.Id())
			);
		}

		if (operationContext.PermanentlyDelete)
		{
			RemoveFromCache(aggregate);
			return new SaveOutcome<T>(aggregate, SaveOutcomeKind.Written);
		}

		Deleted deleteAggregateEvent = new();
		EventMetadata metadata = new(
			aggregate.Details.CurrentVersion + 1,
			DateTimeOffset.UtcNow,
			SchemaVersion: 1,
			IdempotencyId: null,
			CorrelationId: null,
			CausationId: null,
			UserId: null
		);
		aggregate.ApplyEvent(deleteAggregateEvent, metadata);

		await AddToCacheAsync(aggregate, additionalEvents: new EventRecord(deleteAggregateEvent, metadata));

		return new SaveOutcome<T>(aggregate, SaveOutcomeKind.Written);
	}

	void RemoveFromCache(T aggregate)
	{
		_aggregates.TryRemove(aggregate.Id(), out _);
		_tenants.TryRemove(aggregate.Id(), out _);
		if (_events.TryRemove(aggregate.Id(), out var events))
			events.Clear();
	}

	Task<T> AddToCacheAsync(T aggregate, string? userId = null, params EventRecord[] additionalEvents) =>
		AddToCache(aggregate, true, userId, additionalEvents);

	Task<T> AddToCache(
		T aggregate,
		bool fulfilRequirements,
		string? userId = null,
		params EventRecord[] additionalEvents
	)
	{
		// Another tenant writing into an existing stream would silently take ownership of it.
		if (!CanWrite(aggregate.Id()))
			throw new Exceptions.ConcurrencyException(aggregate.Id(), $"{Guid.NewGuid()}", 0, 0);

		var events = _events.GetOrAdd(aggregate.Id(), _ => new());
		var unsavedEvents = aggregate.GetUnsavedEvents();
		var toPersist = additionalEvents is { Length: > 0 } ? unsavedEvents.Concat(additionalEvents) : unsavedEvents;

		// Events carry the metadata they were recorded with, which predates knowing who is saving.
		// The persistent providers stamp the user onto the stored row; do the same here so the
		// in-memory store reports the same metadata on read-back.
		if (userId != null)
			toPersist = toPersist.Select(record => record with { Metadata = record.Metadata with { UserId = userId } });

		foreach (var record in toPersist)
		{
			// A conflicting version means another write already persisted this aggregate
			// version. Surface it as a concurrency conflict rather than silently dropping the
			// event, which would lose data.
			if (!events.TryAdd(record.Metadata.AggregateVersion, record))
				throw new Exceptions.ConcurrencyException(
					aggregate.Id(),
					record.Metadata.IdempotencyId ?? $"{Guid.NewGuid()}",
					record.Metadata.AggregateVersion,
					!events.IsEmpty ? events.Keys.Max() : aggregate.Details.SavedVersion
				);
		}

		aggregate.ClearUnsavedEvents();

		_tenants[aggregate.Id()] = _currentTenant.Current.TenantId;
		_aggregates.AddOrUpdate(aggregate.Id(), aggregate, (key, existingAggregate) => aggregate);

		if (fulfilRequirements)
			FulfilRequirements(aggregate);

		return Task.FromResult(aggregate);
	}

	///<inheritdoc/>
	public async Task<AggregateExistence> ExistsAsync(string aggregateId, CancellationToken cancellationToken = default)
	{
		await Task.CompletedTask;

		return _aggregates.TryGetValue(aggregateId, out var aggregate) && IsVisible(aggregateId)
			? aggregate.Details.IsDeleted
				? AggregateExistence.ExistsInDeletedState with
				{
					Version = aggregate.Details.CurrentVersion,
				}
				: AggregateExistence.Exists with
				{
					Version = aggregate.Details.CurrentVersion,
				}
			: AggregateExistence.DoesNotExist;
	}

	///<inheritdoc/>
	public T FulfilRequirements(T aggregate)
	{
		aggregateRequirementsManager.Fulfil(aggregate);

		return aggregate;
	}

	///<inheritdoc/>
	public IAsyncEnumerable<string> GetAggregateIdsAsync(
		bool includeDeleted,
		CancellationToken cancellationToken = default
	)
	{
		var results = _aggregates.Where(kvp => IsVisible(kvp.Key)).Select(kvp => kvp.Value);
		if (!includeDeleted)
			results = results.Where(a => !a.Details.IsDeleted);

		return results.Select(a => a.Id()).ToAsyncEnumerable();
	}

	///<inheritdoc/>
	public async Task<Result<T, AggregateReadFailure>> GetAsync(
		string aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId, nameof(aggregateId));

		operationContext ??= EventStoreOperationContext.DefaultContext();

		// An existing stream owned by another tenant reports as absent, and must not fall through to the
		// create path below: that would replace the owner's aggregate with an empty one.
		if (_aggregates.ContainsKey(aggregateId) && !IsVisible(aggregateId))
			return NotFound(aggregateId);

		if (_aggregates.TryGetValue(aggregateId, out var aggregate))
		{
			if (!ReturnAggregate(aggregate.Details.IsDeleted, operationContext))
				return Deleted(aggregateId);

			// Cache hit: return the aggregate directly. Requirements are fulfilled when the
			// aggregate is first created/loaded; re-fulfilling here would refresh scoped services
			// but is skipped for the hot read path.
			return aggregate;
		}

		aggregate = new T { Details = { Id = aggregateId } };
		return await AddToCacheAsync(aggregate);
	}

	///<inheritdoc/>
	public async Task<Result<T, AggregateReadFailure>> GetAtAsync(
		string aggregateId,
		long version,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		var existence = await ExistsAsync(aggregateId, cancellationToken);
		if (!existence.DoesExist)
			return NotFound(aggregateId);

		T aggregate = new() { Details = new() { Id = aggregateId } };

		var events = GetEventRangeAsync(aggregateId, 1, version, cancellationToken);
		await foreach (var eventResult in events.WithCancellation(cancellationToken))
			aggregate.ApplyEvent(eventResult.EventRecord.Event, eventResult.EventRecord.Metadata);

		return FulfilRequirements(aggregate);
	}

	///<inheritdoc/>
	public async Task<Result<T, AggregateReadFailure>> GetDeletedAsync(
		string aggregateId,
		CancellationToken cancellationToken = default
	)
	{
		var result = await GetAsync(
			aggregateId,
			new() { DeleteMode = DeleteHandlingMode.ReturnsAggregate },
			cancellationToken
		);
		if (result.IsFailure)
			return result;

		var aggregate = result.Value;

		return aggregate.Details.IsDeleted ? FulfilRequirements(aggregate) : NotDeleted(aggregateId);
	}

	///<inheritdoc/>
	public IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync(
		string aggregateId,
		long versionFrom,
		long? versionTo,
		CancellationToken cancellationToken
	)
	{
		if (!IsVisible(aggregateId) || !_events.TryGetValue(aggregateId, out var eventList))
			return AsyncEnumerable.Empty<(EventRecord EventRecord, string EventType)>();

		// We have some events... so query and return. Materialize once instead of chaining LINQ
		// iterators over the concurrent dictionary for each enumeration.
		var matched = eventList
			.Where(kvp => kvp.Key >= versionFrom && (!versionTo.HasValue || kvp.Key <= versionTo.Value))
			.OrderBy(kvp => kvp.Key)
			.Select(kvp => (kvp.Value, kvp.Value.Event.GetType().Name))
			.ToArray();

		return matched.ToAsyncEnumerable();
	}

	///<inheritdoc/>
	public async Task<Result<T, AggregateReadFailure>> GetOrCreateAsync(
		string? aggregateId,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		if (!string.IsNullOrWhiteSpace(aggregateId))
		{
			var existence = await ExistsAsync(aggregateId, cancellationToken);
			if (existence.DoesExist)
				return await GetAsync(aggregateId, operationContext, cancellationToken);
		}

		return await CreateAsync(aggregateId, cancellationToken);
	}

	///<inheritdoc/>
	public async Task<Result<SaveOutcome<T>, AggregateRestoreFailure>> RestoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		if (aggregate == null)
			throw NullAggregate(aggregate);

		if (!aggregate.Details.IsDeleted)
		{
			return AggregateRestoreFailure.Failure<SaveOutcome<T>>(
				new AggregateNotDeleted(aggregate.AggregateType, aggregate.Id())
			);
		}

		Restored restoreAggregateEvent = new();
		EventMetadata metadata = new(
			aggregate.Details.CurrentVersion + 1,
			DateTimeOffset.UtcNow,
			SchemaVersion: 1,
			IdempotencyId: null,
			CorrelationId: null,
			CausationId: null,
			UserId: null
		);
		aggregate.ApplyEvent(restoreAggregateEvent, metadata);

		if (aggregate.IsNew())
		{
			return AggregateRestoreFailure.Failure<SaveOutcome<T>>(
				new AggregateNotFound(aggregate.AggregateType, aggregate.Id())
			);
		}

		await AddToCacheAsync(aggregate, additionalEvents: new EventRecord(restoreAggregateEvent, metadata));

		return new SaveOutcome<T>(aggregate, SaveOutcomeKind.Written);
	}

	///<inheritdoc/>
	public async Task<Result<SaveOutcome<T>, AggregateSaveFailure>> SaveAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(aggregate);

		// Wrapped rather than inlined: the core has several early returns, and every one of them is an
		// outcome worth recording.
		var saveStopwatch = System.Diagnostics.Stopwatch.StartNew();
		var result = await SaveCoreAsync(aggregate, operationContext, cancellationToken);

		saveStopwatch.Stop();
		_metrics.RecordSave(saveStopwatch.Elapsed.TotalMilliseconds, typeof(T).Name, result);

		return result;
	}

	async Task<Result<SaveOutcome<T>, AggregateSaveFailure>> SaveCoreAsync(
		T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken
	)
	{
		operationContext ??= EventStoreOperationContext.DefaultContext();

		FulfilRequirements(aggregate);
		var validationResult = await GuardAsync(aggregate, cancellationToken);

		if (!validationResult.IsValid)
			return AggregateSaveFailure.Failure<SaveOutcome<T>>(new AggregateInvalid(validationResult));

		if (aggregate.Details.Locked)
		{
			return AggregateSaveFailure.Failure<SaveOutcome<T>>(
				new AggregateLocked(aggregate.AggregateType, aggregate.Id())
			);
		}

		if (string.IsNullOrWhiteSpace(aggregate.Details.Id))
			throw MissingAggregateIdException();

		if (!aggregate.HasUnsavedEvents())
			return new SaveOutcome<T>(aggregate, SaveOutcomeKind.NoChanges);

		var isNew = aggregate.IsNew();
		var previousAggregateVersion = aggregate.Details.CurrentVersion;
		var changeEvents = aggregate.GetUnsavedEvents().ToArray();

		if (
			operationContext.NotificationMode.HasFlag(NotificationModes.BeforeDelete)
			&& changeEvents.Any(record => record.Event is Deleted)
		)
			await aggregateChangeNotifier.BeforeDeleteAsync(aggregate, cancellationToken);
		else if (operationContext.NotificationMode.HasFlag(NotificationModes.BeforeSave))
			await aggregateChangeNotifier.BeforeSaveAsync(aggregate, isNew, cancellationToken);

		if (
			(await ExistsAsync(aggregate.Id(), cancellationToken)).IsDeleted
			&& !changeEvents.Any(record => record.Event is Restored)
		)
		{
			return AggregateSaveFailure.Failure<SaveOutcome<T>>(
				new AggregateDeleted(aggregate.AggregateType, aggregate.Id())
			);
		}

		try
		{
			var userId = _principalAccessor.GetPrincipal()?.FindFirst(operationContext.ClaimIdentifier)?.Value;
			if (operationContext.RequiresValidPrincipalIdentifier && string.IsNullOrWhiteSpace(userId))
				throw new NullReferenceException(
					$"Missing ClaimsPrincipal identifier '{operationContext.ClaimIdentifier}'. Unable to save aggregate."
				);

			await AddToCacheAsync(aggregate, userId);
			_metrics.EventsAppended(changeEvents.Length, typeof(T).Name);

			// Mirror the persistence providers: after a successful save the aggregate must
			// reflect the persisted version so a subsequent mutation on the same instance
			// records events at the correct version.
			aggregate.Details.SavedVersion = aggregate.Details.CurrentVersion = previousAggregateVersion;

			if (aggregateChangeNotifier != null)
			{
				if (
					aggregate.Details.IsDeleted
					&& operationContext.NotificationMode.HasFlag(NotificationModes.AfterDelete)
				)
					await aggregateChangeNotifier.AfterDeleteAsync(aggregate, cancellationToken);
				else if (operationContext.NotificationMode.HasFlag(NotificationModes.AfterSave))
					await aggregateChangeNotifier.AfterSaveAsync(
						aggregate,
						previousAggregateVersion,
						isNew,
						changeEvents,
						cancellationToken
					);
			}
		}
		catch (Exception ex)
		{
			RemoveFromCache(aggregate);

			if (operationContext.NotificationMode.HasFlag(NotificationModes.OnFailure))
			{
				var deleteRequested = changeEvents.Any(record => record.Event is Deleted);
				await aggregateChangeNotifier.FailureAsync(aggregate, deleteRequested, ex, cancellationToken);
			}

			if (ex is Exceptions.ConcurrencyException conflict)
			{
				return AggregateSaveFailure.Failure<SaveOutcome<T>>(
					new ConcurrencyConflict(
						aggregate.AggregateType,
						aggregate.Id(),
						conflict.VersionAttempted,
						conflict.Version
					)
				);
			}

			throw;
		}

		return new SaveOutcome<T>(aggregate, SaveOutcomeKind.Written);
	}

	/// <summary>
	/// Releases the aggregates and events held by the store.
	/// </summary>
	/// <param name="disposing"><see langword="true"/> to release managed resources; otherwise, <see langword="false"/>.</param>
	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			_aggregates.Clear();
			_events.Clear();
		}
	}

	///<inheritdoc/>
	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	async Task<ValidationResult> GuardAsync(T aggregate, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));

		return _validator == null
			? await DefaultAggregateValidator<T>.Instance.ValidateAsync(aggregate, cancellationToken)
			: await _validator.ValidateAsync(aggregate, cancellationToken);
	}
}
