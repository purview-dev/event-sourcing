using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Purview.EventSourcing.Admin.MongoDB;
using Purview.EventSourcing.Admin.Services;

namespace Microsoft.Extensions.DependencyInjection;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		/// Adds transient <see cref="IAdminAggregateQueryService"/>, <see cref="IAdminEventQueryService"/> and
		/// <see cref="IAdminProjectionService"/> registrations backed by MongoDB.
		/// </summary>
		/// <param name="databaseName">The name of the database that holds the event store collections. Defaults to <c>EventStore</c>.</param>
		/// <exception cref="ArgumentException">Thrown when <paramref name="databaseName"/> is blank.</exception>
		public IServiceCollection AddPurviewEventSourcingAdminMongoDB(string databaseName = "EventStore")
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

			services.TryAddTransient<IAdminAggregateQueryService>(sp =>
			{
				var mongoClient = sp.GetRequiredService<IMongoClient>();
				return new MongoDBAdminAggregateQueryService(mongoClient, databaseName);
			});

			services.TryAddTransient<IAdminEventQueryService>(sp =>
			{
				var mongoClient = sp.GetRequiredService<IMongoClient>();
				return new MongoDBAdminEventQueryService(mongoClient, databaseName);
			});

			services.TryAddTransient<IAdminProjectionService>(sp =>
			{
				var mongoClient = sp.GetRequiredService<IMongoClient>();
				return new MongoDBAdminProjectionService(mongoClient, databaseName);
			});

			services.TryAddTransient<IAdminStorageProbe>(sp =>
			{
				var mongoClient = sp.GetRequiredService<IMongoClient>();
				return new MongoDBAdminStorageProbe(mongoClient, databaseName);
			});

			return services;
		}

		/// <summary>
		/// Replaces the in-memory audit logger with a durable MongoDB one, and registers the reader
		/// that reads it back.
		/// </summary>
		/// <param name="configure">Optional <see cref="MongoDBAdminAuditLoggerOptions"/> configuration.</param>
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
		public IServiceCollection AddPurviewEventSourcingAdminMongoDBAudit(
			Action<MongoDBAdminAuditLoggerOptions>? configure = null
		)
		{
			services.AddKeyedSingleton<IAdminAuditLogger, MongoDBAdminAuditLogger>(AdminAuditKeys.Sink);

			// The reader is registered with the writer, because a trail nothing can read back is not
			// an audit trail. Scoped: it filters on ICurrentTenant, which is resolved per request.
			services.AddScoped<IAdminAuditEntryReader, MongoDBAdminAuditEntryReader>();

			services
				.AddOptions<MongoDBAdminAuditLoggerOptions>()
				.Configure<IConfiguration>(
					(options, configuration) =>
					{
						configuration.GetSection(MongoDBAdminAuditLoggerOptions.MongoDBAudit).Bind(options);
						configure?.Invoke(options);
					}
				);

			return services;
		}
	}
}
