using Purview.Results;

namespace Purview.EventSourcing.Admin.Failures;

/// <summary>
/// The expected ways resolving the tenant for an admin request fails.
/// </summary>
/// <remarks>
/// Both cases are denials rather than errors: neither grants a cross-tenant view. A super user who names
/// no tenant gets <see cref="AdminTenantNotSelected"/>, not the system scope.
/// </remarks>
[GenerateResult]
public readonly union AdminTenantFailure(AdminTenantNotSelected, AdminTenantForbidden);
