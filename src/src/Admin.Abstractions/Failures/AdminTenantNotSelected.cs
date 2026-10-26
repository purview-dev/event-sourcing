namespace Purview.EventSourcing.Admin.Failures;

/// <summary>
/// The user is entitled to more than one tenant but named none, so there is nothing to scope the
/// request to.
/// </summary>
/// <param name="UserId">The identifier of the user, when known.</param>
public readonly record struct AdminTenantNotSelected(string? UserId);
