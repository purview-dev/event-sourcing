using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Subscriptions;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Retention;

/// <summary>
/// Evaluates a retention policy against every visible stream, without removing anything.
/// </summary>
/// <remarks>
/// <para>
/// Provider-neutral: everything it needs already exists on contracts every store implements -
/// stream discovery, the aggregate's snapshot and current versions, and the event range for
/// timestamps. No provider has to implement anything for a preview to be accurate.
/// </para>
/// <para>
/// <strong>It loads every stream.</strong> Reading an aggregate is the only way to learn which
/// version its snapshot covers, so a preview costs roughly a full read of the aggregate type. That
/// is acceptable for a maintenance operation run deliberately, and is the reason this is not
/// something to put on a timer.
/// </para>
/// </remarks>
/// <typeparam name="T">The aggregate type to evaluate.</typeparam>
/// <param name="eventStore">The store to read streams from.</param>
/// <param name="discovery">Supplies the streams to evaluate.</param>
/// <param name="currentTenant">The tenant scope the evaluation runs under.</param>
/// <param name="subscribers">The subscriptions registered in this process.</param>
/// <param name="checkpoints">Where subscription positions are recorded, when any subscription exists.</param>
public sealed class EventStreamRetentionPreview<T>(
	IEventStoreCore<T> eventStore,
	IAggregateStreamDiscovery<T> discovery,
	ICurrentTenant currentTenant,
	IEnumerable<IEventSubscriber<T>> subscribers,
	ISubscriptionCheckpointStore? checkpoints = null
) : IEventStreamRetentionPreview<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public async Task<RetentionPreview> PreviewAsync(EventRetentionPolicy policy, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(policy);
		policy.Validate();

		var scope = currentTenant.Current;
		var subscriptionNames = subscribers.Select(subscriber => subscriber.SubscriptionName).ToArray();
		var ageCutoff = DateTimeOffset.UtcNow - policy.MinimumAge;

		var examined = 0;
		long removable = 0;
		List<RetentionCandidate> eligible = [];
		List<RetentionBlocker> blockers = [];

		await foreach (var aggregateId in discovery.GetAggregateIdsAsync(cancellationToken))
		{
			examined++;

			var read = await eventStore.GetAsync(aggregateId, cancellationToken: cancellationToken);
			if (read.IsFailure)
				continue;

			var details = read.Value.Details;

			// The prune point is the most conservative of the rules, never the most generous.
			var candidate = Math.Min(
				policy.RequireSnapshot ? details.SnapshotVersion : details.CurrentVersion,
				details.CurrentVersion - policy.RetainedVersions
			);

			if (policy.RequireSnapshot && details.SnapshotVersion <= 0)
			{
				blockers.Add(new(aggregateId, RetentionBlockerReason.NoCoveringSnapshot));
				continue;
			}

			if (candidate < 1)
			{
				blockers.Add(new(aggregateId, RetentionBlockerReason.NothingToRemove));
				continue;
			}

			// A subscription that has not read past the prune point could never catch up afterwards,
			// which turns a merely slow projection into a permanently incomplete one, silently.
			var lagging = await FindLaggingSubscriptionAsync(
				subscriptionNames,
				scope,
				aggregateId,
				candidate,
				cancellationToken
			);
			if (lagging is not null)
			{
				blockers.Add(new(aggregateId, RetentionBlockerReason.SubscriptionBehind, lagging));
				continue;
			}

			var withinAge = await IsWithinMinimumAgeAsync(aggregateId, candidate, ageCutoff, cancellationToken);
			if (withinAge)
			{
				blockers.Add(new(aggregateId, RetentionBlockerReason.TooRecent));
				continue;
			}

			eligible.Add(new(aggregateId, candidate, candidate));
			removable += candidate;
		}

		return new(examined, removable, eligible, blockers);
	}

	async Task<string?> FindLaggingSubscriptionAsync(
		string[] subscriptionNames,
		TenantScope scope,
		string aggregateId,
		long pruneThroughVersion,
		CancellationToken cancellationToken
	)
	{
		if (checkpoints is null || subscriptionNames.Length == 0)
			return null;

		foreach (var name in subscriptionNames)
		{
			var position = await checkpoints.GetAsync(new(name, scope, aggregateId), cancellationToken);

			// A subscription that has never read this stream is behind by definition: everything is
			// still ahead of it.
			if ((position ?? 0) < pruneThroughVersion)
				return name;
		}

		return null;
	}

	async Task<bool> IsWithinMinimumAgeAsync(
		string aggregateId,
		long pruneThroughVersion,
		DateTimeOffset ageCutoff,
		CancellationToken cancellationToken
	)
	{
		if (eventStore is not IAggregateEventHistoryStoreCore<T> history)
			// Without an event range the age cannot be established, and an unknown age is treated as
			// too recent: the conservative answer is the only safe one here.
			return true;

		// The newest event that would be removed decides the whole range's age.
		await foreach (
			var (record, _) in history.GetEventRangeAsync(
				aggregateId,
				pruneThroughVersion,
				pruneThroughVersion,
				cancellationToken
			)
		)
			return record.Metadata.When > ageCutoff;

		return true;
	}
}
