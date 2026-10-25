using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
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
	}
}
