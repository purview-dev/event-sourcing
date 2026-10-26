using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

		/// <summary>
		/// Replaces the in-memory audit logger with a durable SQL Server one.
		/// </summary>
		/// <param name="configure">Optional <see cref="SqlServerAdminAuditLoggerOptions"/> configuration.</param>
		/// <returns>The configured builder, allowing further chaining.</returns>
		/// <remarks>
		/// <para>
		/// The default audit logger keeps entries in memory, so the record of who read which payload or
		/// triggered which rebuild dies with the process. In a regulated deployment the audit trail is
		/// the control, which makes that unusable.
		/// </para>
		/// <para>
		/// Audit writes default to <c>AdminAuditFailureMode.Fail</c>: if the entry cannot be written the
		/// operation is refused, because an action nobody can prove happened is worse than an action
		/// that did not happen. Set the failure mode to <c>Continue</c> where availability matters more.
		/// </para>
		/// </remarks>
		public TBuilder AddPurviewEventSourcingAdminSqlServerAudit(
			Action<SqlServerAdminAuditLoggerOptions>? configure = null
		)
		{
			builder.Services.AddKeyedSingleton<IAdminAuditLogger, SqlServerAdminAuditLogger>(AdminAuditKeys.Sink);

			builder
				.Services.AddOptions<SqlServerAdminAuditLoggerOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
					{
						configuration.GetSection(SqlServerAdminAuditLoggerOptions.SqlServerAudit).Bind(options);
						configure?.Invoke(options);
					}
				);

			return builder;
		}
	}
}
