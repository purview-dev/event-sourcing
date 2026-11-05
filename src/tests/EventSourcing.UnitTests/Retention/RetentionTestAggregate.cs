using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.Retention;

/// <summary>
/// A minimal aggregate for the retention preview tests: it only needs a stream of known length.
/// </summary>
public sealed class RetentionTestAggregate : AggregateBase
{
	/// <summary>How many times the aggregate has been touched.</summary>
	public int TouchCount { get; private set; }

	/// <inheritdoc/>
	protected override void RegisterEvents() => Register<RetentionTouched>(_ => TouchCount++);

	/// <summary>Records one event.</summary>
	public void Touch() => RecordAndApply(new RetentionTouched());
}

/// <summary>An event with no payload beyond its metadata.</summary>
[EventContract]
public sealed record RetentionTouched
{
	/// <summary>The event schema version.</summary>
	public static int SchemaVersion => 1;

	/// <summary>The event metadata.</summary>
	public EventMetadata Metadata { get; init; }
}
