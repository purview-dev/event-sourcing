using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Retention;

/// <summary>
/// Removes events from a stream. Implemented by a provider; never called directly.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the dumbest contract that can work: it deletes what it is told to, and makes no
/// decision about whether deleting is safe. Every rule - the covering snapshot, the age floor, the
/// retained margin, the lagging subscription - is evaluated once, in
/// <see cref="IEventStreamRetentionPreview{T}"/>, and a provider that re-derived any of it could
/// disagree with the preview an operator approved.
/// </para>
/// <para>
/// Call <see cref="IEventStreamRetention{T}"/> instead. This exists so a provider can supply the
/// delete, not so callers can skip the rules.
/// </para>
/// </remarks>
/// <typeparam name="T">The aggregate type.</typeparam>
public interface IEventStreamPruning<T>
	where T : class, IAggregate, new()
{
	/// <summary>
	/// Removes every event for the aggregate up to and including <paramref name="throughVersion"/>.
	/// </summary>
	/// <param name="aggregateId">The stream to prune.</param>
	/// <param name="throughVersion">The highest version to remove, inclusive.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The number of events removed.</returns>
	/// <remarks>
	/// Honours the current tenant: a stream belonging to another tenant must not be touched, and a
	/// bulk delete that forgot the scope would destroy data rather than expose it.
	/// </remarks>
	Task<int> PruneEventsAsync(string aggregateId, long throughVersion, CancellationToken cancellationToken);
}
