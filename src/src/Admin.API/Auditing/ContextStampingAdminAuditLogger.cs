using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Admin.API.Auditing;

/// <summary>
/// Fills in the tenant and correlation identifier on an audit entry before passing it on.
/// </summary>
/// <remarks>
/// <para>
/// Stamped centrally rather than at each call site, for the same reason a tenant filter belongs in
/// one place: an endpoint added later cannot forget it. An entry that already carries either value
/// is left alone, so a caller that knows better still wins.
/// </para>
/// <para>
/// The tenant comes from the scope <see cref="Endpoints.AdminTenantScopeFilter"/> establishes for
/// the request, so it records the tenant the operation <em>acted on</em> - the question an
/// investigation asks - rather than every tenant the administrator happens to be entitled to.
/// </para>
/// </remarks>
/// <param name="inner">The logger that records the entry.</param>
/// <param name="currentTenant">The tenant scope the request is running under.</param>
/// <param name="correlationIdProvider">The ambient correlation identifier.</param>
public sealed class ContextStampingAdminAuditLogger(
	IAdminAuditLogger inner,
	ICurrentTenant currentTenant,
	IEventStoreCorrelationIdProvider? correlationIdProvider
) : IAdminAuditLogger
{
	/// <inheritdoc/>
	public Task LogAsync(AdminAuditEntry entry, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entry);

		var scope = currentTenant.Current;

		return inner.LogAsync(
			entry with
			{
				TenantId = entry.TenantId ?? (scope.IsSystem ? null : scope.TenantId),
				CorrelationId = entry.CorrelationId ?? correlationIdProvider?.GetCorrelationId(),
			},
			cancellationToken
		);
	}
}
