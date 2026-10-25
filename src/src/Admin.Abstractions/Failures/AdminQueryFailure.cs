using Purview.Results;

namespace Purview.EventSourcing.Admin.Failures;

/// <summary>
/// The expected ways an admin query fails.
/// </summary>
/// <remarks>
/// Deliberately separate from the event store's own failure unions: the admin abstractions carry no reference
/// to the store, so a host can implement these services over anything.
/// </remarks>
[GenerateResult]
public readonly union AdminQueryFailure(AdminAggregateNotFound);
