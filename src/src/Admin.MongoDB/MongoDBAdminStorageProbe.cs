using MongoDB.Bson;
using MongoDB.Driver;
using Purview.EventSourcing.Admin.Services;

namespace Purview.EventSourcing.Admin.MongoDB;

/// <summary>
/// Reports whether the configured MongoDB event store is reachable.
/// </summary>
/// <param name="mongoClient">The client used to reach the server.</param>
/// <param name="databaseName">The database holding the event-store collections.</param>
public sealed class MongoDBAdminStorageProbe(IMongoClient mongoClient, string databaseName) : IAdminStorageProbe
{
	/// <inheritdoc/>
	public async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
	{
		// ping is the server's own liveness command: no collection access, no read permissions needed.
		var response = await mongoClient
			.GetDatabase(databaseName)
			.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);

		return response.GetValue("ok", BsonValue.Create(0)).ToDouble() is 1d;
	}
}
