using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Purview.EventSourcing.MongoDB.Events;
using Purview.EventSourcing.Subscriptions;

namespace Purview.EventSourcing.MongoDB.Subscriptions;

/// <summary>
/// A durable <see cref="ISubscriptionCheckpointStore"/> backed by a MongoDB collection.
/// </summary>
/// <remarks>
/// Without a durable store every subscription replays every stream after a restart, so this is what
/// makes a subscription usable in a deployed application.
/// </remarks>
public sealed class MongoDBSubscriptionCheckpointStore : ISubscriptionCheckpointStore, IDisposable
{
	const string VersionField = "v";
	const string UpdatedUtcField = "u";

	readonly MongoClient _client;
	readonly IMongoCollection<BsonDocument> _collection;

	/// <summary>Creates the store.</summary>
	/// <param name="checkpointOptions">The checkpoint options.</param>
	/// <param name="eventStoreOptions">The event-store options, used for any value not overridden.</param>
	public MongoDBSubscriptionCheckpointStore(
		IOptions<MongoDBSubscriptionCheckpointStoreOptions> checkpointOptions,
		IOptions<MongoDBEventStoreOptions> eventStoreOptions
	)
	{
		ArgumentNullException.ThrowIfNull(checkpointOptions);
		ArgumentNullException.ThrowIfNull(eventStoreOptions);

		var options = checkpointOptions.Value;
		var connectionString =
			options.ConnectionString
			?? eventStoreOptions.Value.ConnectionString
			?? throw new InvalidOperationException(
				"MongoDBSubscriptionCheckpointStore requires a connection string. Configure 'EventStore:MongoDB:Subscriptions:ConnectionString' or 'EventStore:MongoDB:ConnectionString'."
			);

		var database =
			options.Database
			?? eventStoreOptions.Value.Database
			?? throw new InvalidOperationException(
				"MongoDBSubscriptionCheckpointStore requires a database. Configure 'EventStore:MongoDB:Subscriptions:Database' or 'EventStore:MongoDB:Database'."
			);

		_client = new(connectionString);
		_collection = _client.GetDatabase(database).GetCollection<BsonDocument>(options.CollectionName);
	}

	/// <inheritdoc/>
	public void Dispose() => _client.Dispose();

	/// <inheritdoc/>
	public async Task<long?> GetAsync(SubscriptionCheckpointKey key, CancellationToken cancellationToken)
	{
		var document = await _collection
			.Find(Filter(key))
			.Project(Builders<BsonDocument>.Projection.Include(VersionField))
			.FirstOrDefaultAsync(cancellationToken);

		return document is not null && document.TryGetValue(VersionField, out var version) ? version.ToInt64() : null;
	}

	/// <inheritdoc/>
	public async Task SetAsync(SubscriptionCheckpointKey key, long version, CancellationToken cancellationToken)
	{
		// The filter requires the stored version to be lower, so a cycle that re-read an earlier
		// range cannot rewind a position that is already further on - that would redeliver
		// everything in between.
		var filter = Builders<BsonDocument>.Filter.And(
			Filter(key),
			Builders<BsonDocument>.Filter.Lt(VersionField, version)
		);

		var update = Builders<BsonDocument>
			.Update.Set(VersionField, version)
			.Set(UpdatedUtcField, DateTimeOffset.UtcNow.UtcDateTime);

		try
		{
			await _collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken);
		}
		catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
		{
			// An upsert whose filter excludes the existing document tries to insert it instead, and
			// collides on _id. That happens in exactly two cases: a position at or beyond this one is
			// already recorded, or another writer created the row first. Both mean the stored
			// position is already at least as far on as this one, so there is nothing to do.
		}
	}

	// A compound _id rather than a delimited string: it is unique by definition and cannot be made
	// ambiguous by a subscription name, tenant or aggregate id that happens to contain the delimiter.
	static FilterDefinition<BsonDocument> Filter(SubscriptionCheckpointKey key) =>
		Builders<BsonDocument>.Filter.Eq(
			"_id",
			new BsonDocument
			{
				["s"] = key.SubscriptionName,
				["t"] = SubscriptionCheckpointTenantColumn.Encode(key.Scope),
				["a"] = key.AggregateId,
			}
		);
}
