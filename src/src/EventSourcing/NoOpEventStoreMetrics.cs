namespace Purview.EventSourcing;

/// <summary>
/// An <see cref="IEventStoreMetrics"/> that records nothing.
/// </summary>
/// <remarks>
/// Used when a store is constructed directly rather than resolved from dependency injection, where there
/// is no <see cref="System.Diagnostics.Metrics.IMeterFactory"/> to build instruments from. Recording
/// metrics is never a precondition for persisting events.
/// </remarks>
public sealed class NoOpEventStoreMetrics : IEventStoreMetrics
{
	/// <summary>
	/// The shared instance.
	/// </summary>
	public static readonly NoOpEventStoreMetrics Instance = new();

	NoOpEventStoreMetrics() { }

	/// <inheritdoc/>
	public void SaveDuration(double milliseconds, string aggregateType, string outcome) { }

	/// <inheritdoc/>
	public void GetDuration(double milliseconds, string aggregateType) { }

	/// <inheritdoc/>
	public void EventsAppended(int count, string aggregateType) { }
}
