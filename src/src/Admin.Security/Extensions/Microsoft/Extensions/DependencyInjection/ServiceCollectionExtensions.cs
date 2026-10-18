using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Admin.Security.Handlers;
using Purview.EventSourcing.Admin.Security.Providers;

namespace Microsoft.Extensions.DependencyInjection;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		/// Registers the admin portal permission provider and authorization handlers with the service collection.
		/// </summary>
		/// <param name="permissionProvider">
		/// The permission provider to use, or <see langword="null"/> to register a deny-by-default provider.
		/// </param>
		/// <returns>The configured service collection.</returns>
		public IServiceCollection AddPurviewEventSourcingAdminSecurity(
			IAdminPermissionProvider? permissionProvider = null
		)
		{
			// Deny-by-default if no provider supplied
			services.AddSingleton(permissionProvider ?? new DenyAllPermissionProvider());

			// Register authorization handlers
			services.AddScoped<IAuthorizationHandler, AdminFeatureAuthorizationHandler>();
			services.AddScoped<IAuthorizationHandler, AggregateTypeAccessHandler>();

			// Default in-memory audit logger; applications may replace it with a durable implementation.
			services.TryAddSingleton<IAdminAuditLogger, InMemoryAdminAuditLogger>();

			return services;
		}
	}
}
