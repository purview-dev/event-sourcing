using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.MongoDB.Events;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.MongoDB.Cryptography;

/// <summary>
/// A durable <see cref="IEventCryptoKeyStore"/> backed by a MongoDB collection.
/// </summary>
/// <remarks>
/// <para>
/// Keys are scoped to the current tenant: the same data-subject identifier in two tenants is two
/// different people, so they get two different keys, and neither tenant can read or erase the
/// other's.
/// </para>
/// <para>
/// <strong>Erasure is only as irreversible as the least-irreversible copy of the key.</strong>
/// Restoring this collection from a backup, or a replica that has not yet applied the delete,
/// brings shredded keys back.
/// </para>
/// </remarks>
public sealed class MongoDBEventCryptoKeyStore : IEventCryptoKeyStore, IDisposable
{
	const string KeyMaterialField = "k";
	const string CreatedUtcField = "c";

	readonly MongoClient _client;
	readonly IMongoCollection<BsonDocument> _collection;
	readonly ICurrentTenant _currentTenant;

	/// <summary>Creates the store.</summary>
	/// <param name="keyStoreOptions">The key-store options.</param>
	/// <param name="eventStoreOptions">The event-store options, used for any value not overridden.</param>
	/// <param name="currentTenant">The tenant scope keys are read and written under.</param>
	public MongoDBEventCryptoKeyStore(
		IOptions<MongoDBEventCryptoKeyStoreOptions> keyStoreOptions,
		IOptions<MongoDBEventStoreOptions> eventStoreOptions,
		ICurrentTenant? currentTenant = null
	)
	{
		ArgumentNullException.ThrowIfNull(keyStoreOptions);
		ArgumentNullException.ThrowIfNull(eventStoreOptions);

		var options = keyStoreOptions.Value;
		var connectionString =
			options.ConnectionString
			?? eventStoreOptions.Value.ConnectionString
			?? throw new InvalidOperationException(
				"MongoDBEventCryptoKeyStore requires a connection string. Configure 'EventStore:MongoDB:Cryptography:ConnectionString' or 'EventStore:MongoDB:ConnectionString'."
			);

		var database =
			options.Database
			?? eventStoreOptions.Value.Database
			?? throw new InvalidOperationException(
				"MongoDBEventCryptoKeyStore requires a database. Configure 'EventStore:MongoDB:Cryptography:Database' or 'EventStore:MongoDB:Database'."
			);

		_currentTenant = currentTenant ?? new SystemCurrentTenant();
		_client = new(connectionString);
		_collection = _client.GetDatabase(database).GetCollection<BsonDocument>(options.CollectionName);
	}

	/// <inheritdoc/>
	public async Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		// $setOnInsert with an upsert is atomic get-or-create: concurrent callers converge on one
		// key rather than each generating their own, which would make payloads written by the loser
		// unreadable. ReturnDocument.After yields the stored key whether it was just created or
		// already there.
		var update = Builders<BsonDocument>
			.Update.SetOnInsert(KeyMaterialField, AesGcmEventPayloadProtector.CreateKey())
			.SetOnInsert(CreatedUtcField, DateTimeOffset.UtcNow.UtcDateTime);

		var document = await _collection.FindOneAndUpdateAsync(
			Filter(dataSubjectId),
			update,
			new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
			cancellationToken
		);

		return document[KeyMaterialField].AsByteArray;
	}

	/// <inheritdoc/>
	public async Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		var document = await _collection.Find(Filter(dataSubjectId)).FirstOrDefaultAsync(cancellationToken);

		return document is not null && document.TryGetValue(KeyMaterialField, out var key) ? key.AsByteArray : null;
	}

	/// <inheritdoc/>
	public async Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		var result = await _collection.DeleteOneAsync(Filter(dataSubjectId), cancellationToken);

		return result.DeletedCount > 0;
	}

	// A compound _id rather than a delimited string: it is unique by construction and cannot be made
	// ambiguous by a tenant or subject identifier that happens to contain the delimiter.
	FilterDefinition<BsonDocument> Filter(string dataSubjectId) =>
		Builders<BsonDocument>.Filter.Eq(
			"_id",
			new BsonDocument { ["t"] = TenantColumn.Encode(_currentTenant.Current), ["s"] = dataSubjectId }
		);

	/// <inheritdoc/>
	public void Dispose() => _client.Dispose();
}
