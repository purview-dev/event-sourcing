using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Purview.EventSourcing.Admin.Services;

namespace Microsoft.Extensions.Hosting;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class HostApplicationBuilderExtensions
{
	extension<TBuilder>(TBuilder builder)
		where TBuilder : IHostApplicationBuilder
	{
		/// <summary>
		/// Adds transient <see cref="IAdminAggregateQueryService"/>, <see cref="IAdminEventQueryService"/> and
		/// <see cref="IAdminProjectionService"/> registrations backed by Azure Table Storage.
		/// </summary>
		public TBuilder AddPurviewEventSourcingAdminAzureStorage()
		{
			builder.Services.TryAddTransient<IAdminAggregateQueryService, AzureStorageAdminAggregateQueryService>();
			builder.Services.TryAddTransient<IAdminEventQueryService, AzureStorageAdminEventQueryService>();
			builder.Services.TryAddTransient<IAdminProjectionService, AzureStorageAdminProjectionService>();

			return builder;
		}
	}
}
