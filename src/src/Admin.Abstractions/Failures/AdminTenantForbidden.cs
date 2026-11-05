namespace Purview.EventSourcing.Admin.Failures;

/// <summary>
/// The user named a tenant they are not entitled to.
/// </summary>
/// <param name="UserId">The identifier of the user, when known.</param>
/// <param name="RequestedTenantId">The tenant the user asked for.</param>
public readonly record struct AdminTenantForbidden(string? UserId, string RequestedTenantId);
