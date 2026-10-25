using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Identifies one subscription's position in one aggregate's stream, for one tenant.
/// </summary>
/// <remarks>
/// The <see cref="TenantScope"/> is part of the key rather than something a store is trusted to
/// apply, so a checkpoint cannot be written without stating which tenant it belongs to. Two tenants
/// running the same subscription therefore hold separate positions; if they shared one, whichever
/// tenant ran first would advance the position past the other's unread events and those events
/// would never be delivered.
/// </remarks>
/// <param name="SubscriptionName">The subscription's stable name.</param>
/// <param name="Scope">The tenant the subscription is running as.</param>
/// <param name="AggregateId">The aggregate whose stream the position refers to.</param>
public readonly record struct SubscriptionCheckpointKey(string SubscriptionName, TenantScope Scope, string AggregateId);
