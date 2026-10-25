using Microsoft.Extensions.DependencyInjection.Extensions;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Admin.SQLServer;

namespace Microsoft.Extensions.Hosting;

public static class HostApplicationBuilderExtensions
{
	extension<TBuilder>(TBuilder builder)
		where TBuilder : IHostApplicationBuilder
	{
		/// <summary>
		/// Adds transient <see cref = "IAdminAggregateQueryService"/>, <see cref = "IAdminEventQueryService"/> and
		/// <see cref = "IAdminProjectionService"/> registrations backed by SQL Server.
		/// </summary>
		/// <returns>The configured service collection, allowing further chaining.</returns>
		public TBuilder AddPurviewEventSourcingAdminSqlServer()
		{
			builder.Services.TryAddTransient<IAdminAggregateQueryService, SqlServerAdminAggregateQueryService>();
			builder.Services.TryAddTransient<IAdminStorageProbe, SqlServerAdminStorageProbe>();
			builder.Services.TryAddTransient<IAdminEventQueryService, SqlServerAdminEventQueryService>();
			builder.Services.TryAddTransient<IAdminProjectionService, SqlServerAdminProjectionService>();

			return builder;
		}
	}
}
