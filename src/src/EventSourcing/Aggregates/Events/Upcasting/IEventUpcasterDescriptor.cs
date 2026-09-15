namespace Purview.EventSourcing.Aggregates.Events.Upcasting;

/// <summary>
/// Internal descriptor that bridges the closed-generic <see cref="IEventUpcaster{TSource,TTarget}"/>
/// to a non-generic function that can be stored in a dictionary.
/// </summary>
/// <remarks>
/// Implementations are created automatically by
/// <see cref="EventStoreServiceICollectionExtensions.AddEventUpcaster{TSource,TTarget,TUpcaster}"/>
/// and do not need to be created manually.
/// </remarks>
public interface IEventUpcasterDescriptor
{
	/// <summary>The source (legacy) event type that this upcaster handles.</summary>
	Type SourceType { get; }

	/// <summary>The event type produced by this upcaster.</summary>
	Type TargetType { get; }

	/// <summary>Executes the upcaster and returns the up-cast event.</summary>
	object Upcast(object source);
}
