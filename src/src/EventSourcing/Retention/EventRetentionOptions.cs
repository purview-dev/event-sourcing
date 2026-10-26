namespace Purview.EventSourcing.Retention;

/// <summary>
/// How retention is invoked.
/// </summary>
/// <remarks>
/// Both modes can be enabled together: a deployment may run a schedule for routine trimming and
/// still let an operator prune on demand. They are flags rather than an either/or because they are
/// genuinely independent, and because "neither" is a legitimate answer - registering retention
/// without turning anything on gives a deployment the preview and nothing destructive.
/// </remarks>
[Flags]
public enum EventRetentionMode
{
	/// <summary>Nothing prunes. The preview is still available.</summary>
	None = 0,

	/// <summary>
	/// A caller may prune by resolving <see cref="IEventStreamRetention{T}"/> and calling it.
	/// </summary>
	OperatorInvoked = 1 << 0,

	/// <summary>
	/// A hosted service prunes on an interval.
	/// </summary>
	/// <remarks>
	/// Worth turning on only once a deployment has run previews and trusts its policy: a schedule
	/// removes history while nobody is watching, and the preview is what tells you the policy is
	/// wrong.
	/// </remarks>
	Scheduled = 1 << 1,
}

/// <summary>
/// Configuration for event-stream retention.
/// </summary>
public sealed class EventRetentionOptions
{
	/// <summary>The configuration section name used to bind these options.</summary>
	public const string Section = "EventStore:Retention";

	/// <summary>
	/// Which invocation modes are enabled. Defaults to <see cref="EventRetentionMode.None"/>.
	/// </summary>
	/// <remarks>
	/// Nothing is destructive until this says so. Defaulting to anything else would mean adding a
	/// package reference could start deleting history.
	/// </remarks>
	public EventRetentionMode Mode { get; set; } = EventRetentionMode.None;

	/// <summary>The policy the scheduled run applies.</summary>
	public EventRetentionPolicy Policy { get; set; } = new();

	/// <summary>The delay between scheduled runs.</summary>
	public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

	/// <summary>
	/// Validates the option set.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when any option is outside its supported range.</exception>
	public void Validate()
	{
		if (Interval <= TimeSpan.Zero)
			throw new InvalidOperationException("EventRetentionOptions.Interval must be positive.");

		Policy.Validate();
	}
}
