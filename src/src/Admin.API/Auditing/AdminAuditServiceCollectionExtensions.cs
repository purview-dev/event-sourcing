using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Purview.EventSourcing.Admin.Security;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Admin.API.Auditing;

/// <summary>
/// Wires the audit logger so every entry carries its tenant and correlation identifier.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AdminAuditServiceCollectionExtensions
{
	/// <summary>
	/// Registers <see cref="ContextStampingAdminAuditLogger"/> as the <see cref="IAdminAuditLogger"/>,
	/// in front of whichever recording logger is registered under
	/// <see cref="AdminAuditKeys.Sink"/>.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <returns>The <paramref name="services"/> for chaining.</returns>
	/// <remarks>
	/// Order-independent by design. An earlier version decorated whatever was registered at the time,
	/// which broke both ways: called before the logger there was nothing to decorate, and a durable
	/// logger registered afterwards replaced the decorator entirely and lost the stamping silently.
	/// The sink key removes the question.
	/// </remarks>
	public static IServiceCollection AddAdminAuditContextStamping(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		// The in-memory sink is the default only if nothing else claimed the key.
		services.TryAddKeyedSingleton<IAdminAuditLogger, InMemoryAdminAuditLogger>(AdminAuditKeys.Sink);

		services.TryAddSingleton<IAdminAuditLogger>(provider => new ContextStampingAdminAuditLogger(
			provider.GetRequiredKeyedService<IAdminAuditLogger>(AdminAuditKeys.Sink),
			// Resolved leniently: an Admin-only host need not have registered the event-store
			// services, and audit stamping must not be the thing that stops it starting.
			provider.GetService<ICurrentTenant>() ?? new SystemCurrentTenant(),
			provider.GetService<IEventStoreCorrelationIdProvider>()
		));

		return services;
	}
}
