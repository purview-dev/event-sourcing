namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Counts how many times one subscription has failed on one event.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the processor is scoped and built fresh for every catch-up cycle, so a counter
/// held inside it would reset on each cycle and never reach any threshold. The tracker is a
/// singleton, and the count therefore spans cycles.
/// </para>
/// <para>
/// <strong>It does not span a restart.</strong> The default implementation keeps counts in memory, so
/// a process that restarts gives a permanently failing event another
/// <see cref="SubscriptionOptions.MaxDeliveryAttempts"/> deliveries before dead-lettering it again.
/// That is the right way round - an event is retried too often rather than abandoned too early - and
/// a deployment that wants exact counts across restarts can implement this over its own store.
/// </para>
/// </remarks>
public interface ISubscriptionDeliveryAttemptTracker
{
	/// <summary>Records a failed delivery and returns the number of attempts so far, including this one.</summary>
	int RecordFailure(SubscriptionCheckpointKey key, long aggregateVersion);

	/// <summary>Forgets an event, once it is delivered or dead-lettered.</summary>
	void Clear(SubscriptionCheckpointKey key, long aggregateVersion);
}
