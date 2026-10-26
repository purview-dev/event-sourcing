using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Retention;

/// <summary>
/// Reports what event-stream retention would remove, without removing it.
/// </summary>
/// <typeparam name="T">The aggregate type to evaluate.</typeparam>
public interface IEventStreamRetentionPreview<T>
	where T : class, IAggregate, new()
{
	/// <summary>
	/// Evaluates <paramref name="policy"/> against every stream visible to the current tenant.
	/// </summary>
	/// <param name="policy">The retention rules.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>What would be removed, and what is holding the rest back.</returns>
	Task<RetentionPreview> PreviewAsync(EventRetentionPolicy policy, CancellationToken cancellationToken);
}
