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
	/// Validates the option set.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when any option is outside its supported range.</exception>
	public void Validate()
	{
		if (PollInterval <= TimeSpan.Zero)
			throw new InvalidOperationException("SubscriptionOptions.PollInterval must be positive.");
		if (CheckpointInterval < 1)
			throw new InvalidOperationException("SubscriptionOptions.CheckpointInterval must be >= 1.");
	}
}
