using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Removes checkpoint rows a subscription no longer needs.
/// </summary>
/// <remarks>
/// <para>
/// A checkpoint row is written per subscription, per tenant, per aggregate. Nothing removed them
/// before this, so retiring a subscription left a row for every stream it had ever read, and pruning
/// an aggregate's stream left its position behind - both for the life of the deployment, on a table
/// that is otherwise only ever written to.
/// </para>
/// <para>
/// <strong>Removing a checkpoint is not neutral.</strong> A subscription that is still running and
/// loses its position restarts that stream from version 1 and redelivers every event in it. Delivery
/// is at-least-once and subscribers are required to be idempotent, so that is a cost rather than a
/// corruption - but it is a cost, and it is why this is operator-invoked and not scheduled.
/// </para>
/// <para>
/// <strong>There is deliberately no wildcard.</strong> Each call names one subscription and one
/// <see cref="TenantScope"/>, matching how a checkpoint is read and written. Removing every tenant's
/// rows means calling it once per tenant. A "delete everything for this subscription" switch is how
/// a production deployment replays itself by accident.
/// </para>
/// </remarks>
public interface ISubscriptionCheckpointMaintenance
{
	/// <summary>
	/// Removes every checkpoint a subscription holds within one tenant scope.
	/// </summary>
	/// <param name="subscriptionName">The subscription being retired.</param>
	/// <param name="scope">
	/// The scope whose rows are removed. Matched exactly: <see cref="TenantScope.System"/> removes
	/// the system scope's own rows, not every tenant's.
	/// </param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The number of checkpoints removed.</returns>
	Task<int> RemoveSubscriptionAsync(string subscriptionName, TenantScope scope, CancellationToken cancellationToken);

	/// <summary>
	/// Removes one subscription's position in one aggregate's stream.
	/// </summary>
	/// <param name="key">The subscription, tenant and aggregate whose position is removed.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns><see langword="true"/> when a checkpoint was removed.</returns>
	/// <remarks>
	/// For a stream that has been deleted or pruned away entirely. Calling it for a stream that still
	/// exists redelivers that stream from the beginning.
	/// </remarks>
	Task<bool> RemoveCheckpointAsync(SubscriptionCheckpointKey key, CancellationToken cancellationToken);
}
