namespace Purview.EventSourcing.Retention;

/// <summary>
/// The rules deciding how much of an aggregate's event stream may be removed.
/// </summary>
/// <remarks>
/// Every value is a guard rather than a target: the prune point is the <em>most conservative</em>
/// version all three allow. Removing an event is the one operation that can destroy business state
/// permanently, so the defaults are deliberately cautious.
/// </remarks>
public sealed record EventRetentionPolicy
{
	/// <summary>The configuration section name used to bind this policy.</summary>
	public const string Section = "EventStore:Retention";

	/// <summary>
	/// Events newer than this are never removed, whatever the other rules allow.
	/// </summary>
	public TimeSpan MinimumAge { get; init; } = TimeSpan.FromDays(90);

	/// <summary>
	/// How many events are always kept behind the snapshot, as a margin.
	/// </summary>
	/// <remarks>
	/// Pruning exactly up to the snapshot leaves no room for a snapshot that turns out to be
	/// unreadable - a schema the current code can no longer deserialize, say. These versions are the
	/// difference between a bad snapshot being an inconvenience and being data loss.
	/// </remarks>
	public int RetainedVersions { get; init; } = 100;

	/// <summary>
	/// Refuses to prune a stream unless a snapshot covers the prune point.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <strong>Treat this as read-only.</strong> Pruning with no covering snapshot destroys state
	/// irrecoverably and inverts the framework's first invariant - that events are the source of
	/// truth and snapshots are a replaceable optimisation. Retention is the one place that
	/// relationship reverses, and it may only do so when the snapshot provably exists.
	/// </para>
	/// <para>
	/// It exists as a property so the preview can report what a deployment has configured, not so it
	/// can be switched off casually.
	/// </para>
	/// </remarks>
	public bool RequireSnapshot { get; init; } = true;

	/// <summary>
	/// Validates the policy.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when any value is outside its supported range.</exception>
	public void Validate()
	{
		if (MinimumAge <= TimeSpan.Zero)
			throw new InvalidOperationException("EventRetentionPolicy.MinimumAge must be positive.");
		if (RetainedVersions < 0)
			throw new InvalidOperationException("EventRetentionPolicy.RetainedVersions must not be negative.");
	}
}
