using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Purview.EventSourcing.Admin.Postgres;
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

		/// <summary>
		/// Replaces the in-memory audit logger with a durable PostgreSQL one.
		/// </summary>
		/// <param name="configure">Optional <see cref="PostgresAdminAuditLoggerOptions"/> configuration.</param>
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
		public TBuilder AddPurviewEventSourcingAdminPostgresAudit(
			Action<PostgresAdminAuditLoggerOptions>? configure = null
		)
		{
			builder.Services.AddKeyedSingleton<IAdminAuditLogger, PostgresAdminAuditLogger>(AdminAuditKeys.Sink);

			// The reader is registered with the writer, because a trail nothing can read back is not
			// an audit trail. Scoped: it filters on ICurrentTenant, which is resolved per request.
			builder.Services.AddScoped<IAdminAuditEntryReader, PostgresAdminAuditEntryReader>();

			builder
				.Services.AddOptions<PostgresAdminAuditLoggerOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
					{
						configuration.GetSection(PostgresAdminAuditLoggerOptions.PostgresAudit).Bind(options);
						configure?.Invoke(options);
					}
				);

			return builder;
		}
	}
}
