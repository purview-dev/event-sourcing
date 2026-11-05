using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Retention;

/// <summary>
/// Removes events a retention policy has released, after evaluating the policy.
/// </summary>
/// <typeparam name="T">The aggregate type to prune.</typeparam>
public interface IEventStreamRetention<T>
	where T : class, IAggregate, new()
{
	/// <summary>
	/// Evaluates <paramref name="policy"/> and removes the events it releases.
	/// </summary>
	/// <param name="policy">The retention rules.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>What was removed, and what is still held back.</returns>
	/// <remarks>
	/// <strong>This destroys events permanently.</strong> It decides what to remove through the same
	/// preview an operator can run first, so what a preview reported is what a prune does - run the
	/// preview, read the blockers, then call this.
	/// </remarks>
	Task<RetentionResult> PruneAsync(EventRetentionPolicy policy, CancellationToken cancellationToken);
}
