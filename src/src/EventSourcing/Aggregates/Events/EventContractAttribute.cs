namespace Purview.EventSourcing.Aggregates.Events;

/// <summary>
/// Marks a type as an event contract. Applied to generated and hand-written event types so the
/// framework can identify them without a common interface or base class.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class EventContractAttribute : Attribute { }
