namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// What one catch-up cycle did.
/// </summary>
/// <param name="StreamsVisited">The number of aggregate streams examined.</param>
/// <param name="EventsDelivered">The number of events handed to the subscriber.</param>
/// <param name="StreamsFaulted">
/// The number of streams stopped early because the subscriber threw. Each is retried on the next
/// cycle from the last event it did handle.
/// </param>
public readonly record struct SubscriptionCycleResult(int StreamsVisited, int EventsDelivered, int StreamsFaulted);
