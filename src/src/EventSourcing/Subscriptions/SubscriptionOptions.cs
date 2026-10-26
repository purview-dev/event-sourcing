namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Configuration for event subscriptions.
/// </summary>
public sealed class SubscriptionOptions
{
	/// <summary>The configuration section name used to bind these options.</summary>
	public const string Section = "EventStore:Subscriptions";

	/// <summary>The delay between catch-up cycles.</summary>
	public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// How many events are delivered between checkpoint writes.
	/// </summary>
	/// <remarks>
	/// The trade-off is write volume against redelivery after a crash: a larger interval writes less
	/// and redelivers more. Whatever the value, the position is also written when an aggregate's
	/// stream is exhausted, so a quiet stream does not leave progress unrecorded.
	/// </remarks>
	public int CheckpointInterval { get; set; } = 100;

	/// <summary>
	/// How many times one event may fail before it is offered to an
	/// <see cref="ISubscriptionDeadLetterSink"/>. <c>0</c> disables dead-lettering entirely.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <strong>Zero by default, which means a permanently failing event stalls its stream
	/// forever.</strong> That is deliberate: skipping an event silently is worse, and turning the
	/// escape hatch on should be a decision someone made rather than a default they inherited.
	/// </para>
	/// <para>
	/// Setting this alone changes nothing. The default sink accepts nothing, so dead-lettering needs
	/// both this value and a registered <see cref="ISubscriptionDeadLetterSink"/> - two deliberate
	/// steps before an event can ever be passed over.
	/// </para>
	/// <para>
	/// Attempts are counted per process, not per event for all time; see
	/// <see cref="ISubscriptionDeliveryAttemptTracker"/>.
	/// </para>
	/// </remarks>
	public int MaxDeliveryAttempts { get; set; }

	/// <summary>
	/// Validates the option set.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when any option is outside its supported range.</exception>
	public void Validate()
	{
		if (PollInterval <= TimeSpan.Zero)
			throw new InvalidOperationException("SubscriptionOptions.PollInterval must be positive.");
		if (CheckpointInterval < 1)
			throw new InvalidOperationException("SubscriptionOptions.CheckpointInterval must be >= 1.");
		if (MaxDeliveryAttempts < 0)
			throw new InvalidOperationException(
				"SubscriptionOptions.MaxDeliveryAttempts must be >= 0. Use 0 to disable dead-lettering."
			);
	}
}
