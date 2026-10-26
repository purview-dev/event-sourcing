namespace Purview.EventSourcing;

/// <summary>
/// Removes idempotency markers that are older than the retry window they exist to cover.
/// </summary>
/// <remarks>
/// <para>
/// A marker deduplicates a retried save. Its usefulness expires with the retry window - minutes,
/// not years - after which it is never read again and is pure storage cost. Nothing in the framework
/// removed them before this, so they accumulated for the life of the deployment.
/// </para>
/// <para>
/// <strong>Deleting an expired marker is safe</strong>, which is what makes this separable from
/// event-stream retention. The worst case is that a save retried after the window elapsed is applied
/// twice, and optimistic concurrency already rejects that. Pruning an <em>event</em> is a different
/// proposition entirely - see <c>docs/wiki/Retention-Design.md</c>.
/// </para>
/// <para>
/// Cleanup honours the current tenant: run it under a tenant scope to clear only that tenant's
/// markers, or under <see cref="Tenancy.TenantScope.System"/> to clear them all.
/// </para>
/// </remarks>
public interface IIdempotencyMarkerMaintenance
{
	/// <summary>
	/// Removes markers older than <paramref name="retention"/>.
	/// </summary>
	/// <param name="retention">How long a marker is kept. Must be positive.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The number of markers removed.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown when <paramref name="retention"/> is zero or negative. A zero retention would delete
	/// markers for saves still in flight, reintroducing the duplicate they exist to prevent.
	/// </exception>
	Task<int> CleanupIdempotencyMarkersAsync(TimeSpan retention, CancellationToken cancellationToken);
}
