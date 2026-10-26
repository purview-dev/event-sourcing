using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// A minimal aggregate for the subscription tests: it only needs a stream to exist.
/// </summary>
public sealed class SubscriptionTestAggregate : AggregateBase
{
	/// <summary>How many times the aggregate has been touched.</summary>
	public int TouchCount { get; private set; }

	/// <inheritdoc/>
	protected override void RegisterEvents() => Register<Touched>(_ => TouchCount++);

	/// <summary>Records one event.</summary>
	public void Touch() => RecordAndApply(new Touched());
}

/// <summary>An event with no payload beyond its metadata.</summary>
[EventContract]
public sealed record Touched
{
	/// <summary>The event schema version.</summary>
	public static int SchemaVersion => 1;

	/// <summary>The event metadata.</summary>
	public EventMetadata Metadata { get; init; }
}
