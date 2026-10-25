using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing;

/// <summary>
/// The aggregate the acceptance scenarios act on.
/// </summary>
public sealed class AcceptanceAggregate : AggregateBase
{
	/// <summary>The running total.</summary>
	public int Value { get; private set; }

	/// <inheritdoc/>
	protected override void RegisterEvents() => Register<ValueIncremented>(@event => Value += @event.Amount);

	/// <summary>Records an increment.</summary>
	/// <param name="amount">The amount to add.</param>
	public void Increment(int amount) => RecordAndApply(new ValueIncremented { Amount = amount });
}

/// <summary>An increment to the aggregate's value.</summary>
[EventContract]
public sealed record ValueIncremented
{
	/// <summary>The event schema version.</summary>
	public static int SchemaVersion => 1;

	/// <summary>The event metadata.</summary>
	public EventMetadata Metadata { get; init; }

	/// <summary>The amount added.</summary>
	public int Amount { get; set; }
}
