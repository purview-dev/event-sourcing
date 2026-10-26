using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.Faults;

/// <summary>
/// A minimal aggregate for the failure-invariant tests: it only needs a countable stream.
/// </summary>
public sealed class ResilienceTestAggregate : AggregateBase
{
	/// <summary>How many times the aggregate has been touched.</summary>
	public int TouchCount { get; private set; }

	/// <inheritdoc/>
	protected override void RegisterEvents() => Register<ResilienceTouched>(_ => TouchCount++);

	/// <summary>Records one event.</summary>
	public void Touch() => RecordAndApply(new ResilienceTouched());
}

/// <summary>An event with no payload beyond its metadata.</summary>
[EventContract]
public sealed record ResilienceTouched
{
	/// <summary>The event schema version.</summary>
	public static int SchemaVersion => 1;

	/// <summary>The event metadata.</summary>
	public EventMetadata Metadata { get; init; }
}
