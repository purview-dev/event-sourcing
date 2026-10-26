using Microsoft.Extensions.Logging;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Retention;

/// <summary>
/// Prunes the streams a retention policy releases, using the same preview an operator can run first.
/// </summary>
/// <remarks>
/// <para>
/// The rules are evaluated once, by the preview. This only removes what the preview released, so a
/// prune cannot quietly do more than the dry run an operator approved.
/// </para>
/// <para>
/// Every run is logged, and a run with <see cref="EventRetentionPolicy.RequireSnapshot"/> disabled
/// is logged as a warning naming the risk: that configuration removes events with no snapshot behind
/// them, which is unrecoverable. It is permitted because a deployment may have accepted that, and it
/// is loud because nobody should be able to say afterwards that they were not told.
/// </para>
/// </remarks>
/// <typeparam name="T">The aggregate type to prune.</typeparam>
/// <param name="preview">Decides what may be removed.</param>
/// <param name="pruning">Removes it.</param>
/// <param name="currentTenant">The tenant scope the run is attributed to.</param>
/// <param name="logger">Records what each run did.</param>
public sealed partial class EventStreamRetention<T>(
	IEventStreamRetentionPreview<T> preview,
	IEventStreamPruning<T> pruning,
	ICurrentTenant currentTenant,
	ILogger<EventStreamRetention<T>> logger
) : IEventStreamRetention<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public async Task<RetentionResult> PruneAsync(EventRetentionPolicy policy, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(policy);
		policy.Validate();

		var scope = currentTenant.Current;
		var tenant = scope.IsSystem ? "system" : scope.TenantId!;

		if (!policy.RequireSnapshot)
			LogSnapshotRequirementDisabled(logger, typeof(T).Name, tenant);

		var plan = await preview.PreviewAsync(policy, cancellationToken);

		LogPruneStarting(logger, typeof(T).Name, tenant, plan.Eligible.Count, plan.Blockers.Count);

		var streamsPruned = 0;
		long eventsRemoved = 0;

		foreach (var candidate in plan.Eligible)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var removed = await pruning.PruneEventsAsync(
				candidate.AggregateId,
				candidate.PruneThroughVersion,
				cancellationToken
			);

			if (removed <= 0)
				continue;

			streamsPruned++;
			eventsRemoved += removed;

			// Per stream rather than per run: this is the record of which events stopped existing,
			// and a single total cannot answer "what happened to this aggregate".
			LogStreamPruned(
				logger,
				typeof(T).Name,
				tenant,
				candidate.AggregateId,
				candidate.PruneThroughVersion,
				removed
			);
		}

		LogPruneCompleted(logger, typeof(T).Name, tenant, streamsPruned, eventsRemoved, plan.Blockers.Count);

		return new(plan.StreamsExamined, streamsPruned, eventsRemoved, plan.Blockers);
	}

	[LoggerMessage(
		EventId = 1,
		Level = LogLevel.Warning,
		Message = "Event retention for {AggregateType} in tenant {Tenant} is running with RequireSnapshot disabled. Events will be removed with no snapshot covering them, and the state they carry cannot be reconstructed afterwards."
	)]
	static partial void LogSnapshotRequirementDisabled(ILogger logger, string aggregateType, string tenant);

	[LoggerMessage(
		EventId = 2,
		Level = LogLevel.Information,
		Message = "Event retention for {AggregateType} in tenant {Tenant} starting: {EligibleStreams} stream(s) eligible, {BlockedStreams} held back."
	)]
	static partial void LogPruneStarting(
		ILogger logger,
		string aggregateType,
		string tenant,
		int eligibleStreams,
		int blockedStreams
	);

	[LoggerMessage(
		EventId = 3,
		Level = LogLevel.Information,
		Message = "Event retention removed {EventsRemoved} event(s) from {AggregateType} {AggregateId} in tenant {Tenant}, through version {ThroughVersion}."
	)]
	static partial void LogStreamPruned(
		ILogger logger,
		string aggregateType,
		string tenant,
		string aggregateId,
		long throughVersion,
		int eventsRemoved
	);

	[LoggerMessage(
		EventId = 4,
		Level = LogLevel.Information,
		Message = "Event retention for {AggregateType} in tenant {Tenant} completed: {StreamsPruned} stream(s) pruned, {EventsRemoved} event(s) removed, {BlockedStreams} held back."
	)]
	static partial void LogPruneCompleted(
		ILogger logger,
		string aggregateType,
		string tenant,
		int streamsPruned,
		long eventsRemoved,
		int blockedStreams
	);
}
