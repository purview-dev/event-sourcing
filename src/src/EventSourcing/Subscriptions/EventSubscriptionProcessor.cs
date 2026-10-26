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
	ISubscriptionDeadLetterSink deadLetters,
	ISubscriptionDeliveryAttemptTracker attempts,
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
		var eventsDeadLettered = 0;

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
					var version = record.Metadata.AggregateVersion;

					try
					{
						await subscriber.OnEventAsync(new(aggregateId, record, eventType, scope), cancellationToken);
						attempts.Clear(key, version);
						eventsDelivered++;
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						throw;
					}
					// The subscriber's failure is the one this method is about; it must not be
					// confused with a store failure, which the outer handler still catches.
#pragma warning disable CA1031
					catch (Exception ex)
#pragma warning restore CA1031
					{
						if (!await TryDeadLetterAsync(key, aggregateId, version, eventType, ex, cancellationToken))
							// Not dead-lettered: stop this stream here. The checkpoint is not advanced
							// past the event, so the next cycle retries it rather than skipping it.
							throw;

						eventsDeadLettered++;
					}

					// Reached on delivery and on dead-lettering alike: in both cases this version is
					// accounted for, and leaving the checkpoint behind it would redeliver forever.
					lastDelivered = version;

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

		return new(streamsVisited, eventsDelivered, streamsFaulted, eventsDeadLettered);
	}

	/// <summary>
	/// Decides whether a failed event may be passed over, and records it if so.
	/// </summary>
	/// <remarks>
	/// Three things must all be true before an event is skipped: dead-lettering is enabled, the event
	/// has failed enough times, and a sink has accepted it. Any one of them false leaves the stream
	/// stalled, which is the safe outcome - the stream is the canonical record and has not changed.
	/// </remarks>
	async Task<bool> TryDeadLetterAsync(
		SubscriptionCheckpointKey key,
		string aggregateId,
		long version,
		string eventType,
		Exception failure,
		CancellationToken cancellationToken
	)
	{
		var maximum = options.Value.MaxDeliveryAttempts;
		if (maximum < 1)
			return false;

		var attempted = attempts.RecordFailure(key, version);
		if (attempted < maximum)
			return false;

		DeadLetteredEvent deadLettered = new(
			subscriber.SubscriptionName,
			key.Scope,
			aggregateId,
			version,
			eventType,
			attempted,
			failure.ToString()
		);

		bool accepted;
		try
		{
			accepted = await deadLetters.DeadLetterAsync(deadLettered, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		// A sink that throws has not recorded the failure, so the event must not be passed over. It
		// is reported separately from the subscriber's own failure, because the two call for
		// different fixes.
#pragma warning disable CA1031
		catch (Exception ex)
#pragma warning restore CA1031
		{
			logger.LogError(
				ex,
				"The dead-letter sink refused aggregate {AggregateId} version {Version} for subscription {SubscriptionName}; the stream stays stalled there.",
				aggregateId,
				version,
				subscriber.SubscriptionName
			);

			return false;
		}

		if (!accepted)
			return false;

		attempts.Clear(key, version);
		logger.LogWarning(
			failure,
			"Subscription {SubscriptionName} gave up on aggregate {AggregateId} version {Version} after {Attempts} attempts and dead-lettered it; the stream continues from the next version. The event itself is untouched in the stream.",
			subscriber.SubscriptionName,
			aggregateId,
			version,
			attempted
		);

		return true;
	}
}
