using Microsoft.Extensions.Logging;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// The default sink: it accepts nothing, so a stream never advances past an event the subscriber
/// rejected.
/// </summary>
/// <remarks>
/// A null object rather than an absent registration, so the processor always has something to call
/// and dead-lettering is one registration away rather than a code change. It logs at warning when it
/// refuses, because reaching this point means a stream is stalled and the deployment has not said
/// what should happen to the event - which is worth knowing before someone notices the lag.
/// </remarks>
/// <param name="logger">Reports the refusal.</param>
public sealed partial class NoSubscriptionDeadLetterSink(ILogger<NoSubscriptionDeadLetterSink> logger)
	: ISubscriptionDeadLetterSink
{
	/// <inheritdoc/>
	public Task<bool> DeadLetterAsync(DeadLetteredEvent deadLettered, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(deadLettered);

		LogRefused(
			logger,
			deadLettered.SubscriptionName,
			deadLettered.AggregateId,
			deadLettered.AggregateVersion,
			deadLettered.Attempts
		);

		return Task.FromResult(false);
	}

	[LoggerMessage(
		EventId = 1,
		Level = LogLevel.Warning,
		Message = "Subscription {SubscriptionName} has failed {Attempts} times on aggregate {AggregateId} version {AggregateVersion} and no dead-letter sink is registered, so the stream stays stalled at that version. Register an ISubscriptionDeadLetterSink to let it move on."
	)]
	static partial void LogRefused(
		ILogger logger,
		string subscriptionName,
		string aggregateId,
		long aggregateVersion,
		int attempts
	);
}
