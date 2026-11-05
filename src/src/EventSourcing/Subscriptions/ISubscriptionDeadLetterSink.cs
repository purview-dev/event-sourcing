namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Receives an event a subscriber could not handle, so its stream can move on.
/// </summary>
/// <remarks>
/// <para>
/// Without one, a permanently failing event stalls its stream forever: delivery is at-least-once, so
/// the checkpoint is not advanced past an event the subscriber rejected, and every subsequent cycle
/// retries the same event and gets no further. That is fail-closed and deliberate - the alternative
/// is silently skipping an event - but it is not an answer, because the stream behind the stall goes
/// undelivered too.
/// </para>
/// <para>
/// <strong>The return value is the whole contract.</strong> The checkpoint advances past the event
/// only when this returns <see langword="true"/>. A sink that cannot record the failure must return
/// <see langword="false"/> or throw, and the stream stays stalled - which is the correct outcome,
/// because the alternative is losing the event with nothing anywhere to say it happened.
/// </para>
/// <para>
/// The default registration never accepts anything, so dead-lettering is off until an application
/// both registers a real sink and sets <see cref="SubscriptionOptions.MaxDeliveryAttempts"/>.
/// </para>
/// </remarks>
public interface ISubscriptionDeadLetterSink
{
	/// <summary>Records an event that could not be delivered.</summary>
	/// <param name="deadLettered">What failed, and where to find it again.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>
	/// <see langword="true"/> when the failure is durably recorded and the subscription may advance
	/// past it; <see langword="false"/> to leave the stream stalled.
	/// </returns>
	Task<bool> DeadLetterAsync(DeadLetteredEvent deadLettered, CancellationToken cancellationToken);
}
