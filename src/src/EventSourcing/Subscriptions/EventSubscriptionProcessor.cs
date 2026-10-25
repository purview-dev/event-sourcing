using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Reads each visible aggregate stream from its recorded position and delivers the events in version
/// order.
/// </summary>
/// <remarks>
/// <para>
/// The checkpoint is written after the subscriber returns, never before, so a failure redelivers
/// rather than skips. That makes delivery at-least-once.
/// </para>
/// <para>
/// The cycle runs under whatever <see cref="TenantScope"/> is current. Discovery, the event read and
/// the checkpoint all use that scope, so a tenant-scoped subscription sees only its own aggregates
/// and keeps its own position.
/// </para>
/// </remarks>
/// <typeparam name="T">The aggregate type the subscription reads.</typeparam>
public sealed class EventSubscriptionProcessor<T>(
	IEventSubscriber<T> subscriber,
	ISubscriptionCheckpointStore checkpoints,
	IAggregateStreamDiscovery<T> discovery,
	IEventStoreCore<T> eventStore,
	ICurrentTenant currentTenant,
	IOptions<SubscriptionOptions> options,
	ILogger<EventSubscriptionProcessor<T>> logger
) : IEventSubscriptionProcessor<T>
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	public async Task<SubscriptionCycleResult> RunCycleAsync(CancellationToken cancellationToken)
	{
		if (eventStore is not IAggregateEventHistoryStoreCore<T> history)
		{
			throw new NotSupportedException(
				$"The configured event store for aggregate type '{typeof(T).FullName}' does not persist an event stream, so it cannot be subscribed to."
			);
		}

		var scope = currentTenant.Current;
		var checkpointInterval = options.Value.CheckpointInterval;

		var streamsVisited = 0;
		var eventsDelivered = 0;
		var streamsFaulted = 0;

		await foreach (var aggregateId in discovery.GetAggregateIdsAsync(cancellationToken))
		{
			streamsVisited++;

			SubscriptionCheckpointKey key = new(subscriber.SubscriptionName, scope, aggregateId);
			var lastDelivered = await checkpoints.GetAsync(key, cancellationToken) ?? 0;

			// Aggregate versions start at 1, so a stream never read before resumes at 1.
			var resumeFrom = lastDelivered + 1;
			var sinceCheckpoint = 0;

			try
			{
				await foreach (
					var (record, eventType) in history.GetEventRangeAsync(
						aggregateId,
						resumeFrom,
						versionTo: null,
						cancellationToken
					)
				)
				{
					await subscriber.OnEventAsync(new(aggregateId, record, eventType, scope), cancellationToken);

					lastDelivered = record.Metadata.AggregateVersion;
					eventsDelivered++;

					if (++sinceCheckpoint >= checkpointInterval)
					{
						await checkpoints.SetAsync(key, lastDelivered, cancellationToken);
						sinceCheckpoint = 0;
					}
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// Record what was delivered before unwinding, so the cancelled cycle is not replayed.
				if (sinceCheckpoint > 0)
					await checkpoints.SetAsync(key, lastDelivered, CancellationToken.None);

				throw;
			}
			// One subscriber failure must not abandon the remaining streams.
#pragma warning disable CA1031
			catch (Exception ex)
#pragma warning restore CA1031
			{
				streamsFaulted++;
				logger.LogError(
					ex,
					"Subscription {SubscriptionName} stopped reading aggregate {AggregateId} at version {Version}; it will retry from there on the next cycle.",
					subscriber.SubscriptionName,
					aggregateId,
					lastDelivered + 1
				);
			}

			// Also checkpoint when the stream is exhausted, so progress on a quiet stream is not
			// left unrecorded until the interval happens to be reached.
			if (sinceCheckpoint > 0)
				await checkpoints.SetAsync(key, lastDelivered, cancellationToken);
		}

		return new(streamsVisited, eventsDelivered, streamsFaulted);
	}
}
