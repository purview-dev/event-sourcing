using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Purview.EventSourcing.Admin.AzureStorage;
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
			builder.Services.TryAddTransient<IAdminStorageProbe, AzureStorageAdminStorageProbe>();
			builder.Services.TryAddTransient<IAdminEventQueryService, AzureStorageAdminEventQueryService>();
			builder.Services.TryAddTransient<IAdminProjectionService, AzureStorageAdminProjectionService>();

			return builder;
		}

		/// <summary>
		/// Replaces the in-memory audit logger with a durable Azure Storage one, and registers the
		/// reader that reads it back.
		/// </summary>
		/// <param name="configure">Optional <see cref="AzureStorageAdminAuditLoggerOptions"/> configuration.</param>
		/// <remarks>
		/// <para>
		/// The default audit logger keeps entries in memory, so the record of who read which payload
		/// dies with the process. In a regulated deployment the audit trail is the control, and this
		/// is what makes it survive a restart.
		/// </para>
		/// <para>
		/// The failure mode defaults to <c>Fail</c>: an operation that cannot be audited does not
		/// happen. Set it to <c>Continue</c> where availability matters more than the record.
		/// </para>
		/// </remarks>
		public TBuilder AddPurviewEventSourcingAdminAzureStorageAudit(
			Action<AzureStorageAdminAuditLoggerOptions>? configure = null
		)
		{
			builder.Services.AddKeyedSingleton<IAdminAuditLogger, AzureStorageAdminAuditLogger>(AdminAuditKeys.Sink);

			// The reader is registered with the writer, because a trail nothing can read back is not
			// an audit trail. Scoped: it is partitioned on ICurrentTenant, resolved per request.
			builder.Services.AddScoped<IAdminAuditEntryReader, AzureStorageAdminAuditEntryReader>();

			builder
				.Services.AddOptions<AzureStorageAdminAuditLoggerOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
					{
						configuration.GetSection(AzureStorageAdminAuditLoggerOptions.AzureStorageAudit).Bind(options);
						configure?.Invoke(options);
					}
				);

			return builder;
		}
	}
}
