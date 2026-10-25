using Microsoft.Extensions.DependencyInjection.Extensions;
using Purview.EventSourcing.Admin.Services;

namespace Microsoft.Extensions.Hosting;

public static class HostApplicationBuilderExtensions
{
	extension<TBuilder>(TBuilder builder)
		where TBuilder : IHostApplicationBuilder
	{
		/// <summary>
		/// Adds transient <see cref = "IAdminAggregateQueryService"/>, <see cref = "IAdminEventQueryService"/> and
		/// <see cref = "IAdminProjectionService"/> registrations backed by PostgreSQL.
		/// </summary>
		/// <returns>The configured service collection, allowing further chaining.</returns>
		public TBuilder AddPurviewEventSourcingAdminPostgres()
		{
			builder.Services.TryAddTransient<IAdminAggregateQueryService, PostgresAdminAggregateQueryService>();
			builder.Services.TryAddTransient<IAdminStorageProbe, PostgresAdminStorageProbe>();
			builder.Services.TryAddTransient<IAdminEventQueryService, PostgresAdminEventQueryService>();
			builder.Services.TryAddTransient<IAdminProjectionService, PostgresAdminProjectionService>();

			return builder;
		}
	}
}
