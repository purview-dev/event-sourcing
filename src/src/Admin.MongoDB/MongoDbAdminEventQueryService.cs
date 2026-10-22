using MongoDB.Driver;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Queries;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.MongoDB.Events.Entities;
using System.Text.Json;

namespace Purview.EventSourcing.Admin.MongoDB;

/// <summary>
/// Provides event range queries against MongoDB for the Admin portal.
/// </summary>
/// <remarks>
/// Events are read from the <c>es-{aggregateType}-events</c> collection and exposed as
/// <see cref="EventEnvelopeResponse"/> values. Version, time and paging filters are translated into MongoDB
/// query filters and executed against the database.
/// </remarks>
/// <param name="mongoClient">The MongoDB client used to reach the event store database.</param>
/// <param name="databaseName">The name of the database that holds the event store collections.</param>
public sealed class MongoDBAdminEventQueryService(IMongoClient mongoClient, string databaseName)
	: IAdminEventQueryService
{
	readonly IMongoClient _mongoClient = mongoClient ?? throw new ArgumentNullException(nameof(mongoClient));
	readonly string _databaseName = databaseName ?? throw new ArgumentNullException(nameof(databaseName));

	///<inheritdoc/>
	public async Task<PagedResult<EventEnvelopeResponse>?> GetRangeAsync(
		string aggregateType,
		string aggregateId,
		EventRangeQuery query,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
		ArgumentNullException.ThrowIfNull(query);

		var database = _mongoClient.GetDatabase(_databaseName);
		var collectionName = $"es-{aggregateType}-events";
		var collection = database.GetCollection<EventEntity>(collectionName);

		var filter = Builders<EventEntity>.Filter.And(
			Builders<EventEntity>.Filter.Eq(x => x.AggregateId, aggregateId),
			Builders<EventEntity>.Filter.Eq(x => x.EntityType, EntityTypes.EventType)
		);

		if (query.VersionFrom.HasValue && query.VersionFrom > 0)
		{
			filter &= Builders<EventEntity>.Filter.Gte(x => x.Version, query.VersionFrom.Value);
		}

		if (query.VersionTo.HasValue && query.VersionTo > 0)
		{
			filter &= Builders<EventEntity>.Filter.Lte(x => x.Version, query.VersionTo.Value);
		}

		if (query.TimeFromUtc.HasValue)
		{
			filter &= Builders<EventEntity>.Filter.Gte(x => x.Timestamp, query.TimeFromUtc.Value);
		}

		if (query.TimeToUtc.HasValue)
		{
			filter &= Builders<EventEntity>.Filter.Lte(x => x.Timestamp, query.TimeToUtc.Value);
		}

		var pageSize = Math.Max(1, Math.Min(query.PageSize, 500));
		var skip = (query.Page - 1) * pageSize;

		var total = await collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
		if (total == 0)
		{
			return new PagedResult<EventEnvelopeResponse>([], query.Page, pageSize, 0);
		}

		var events = await collection
			.Find(filter)
			.Skip(skip)
			.Limit(pageSize)
			.SortBy(x => x.Version)
			.ToListAsync(cancellationToken);

		var envelopes = events
			.Select(e => new EventEnvelopeResponse(
				aggregateType,
				e.AggregateId,
				new EventMetadataResponse(
					e.Version,
					e.Timestamp ?? DateTimeOffset.UtcNow,
					e.EventType ?? "Unknown",
					e.SchemaVersion,
					e.CorrelationId,
					e.CausationId,
					e.IdempotencyId,
					e.UserId
				),
				ParsePayload(e.Payload)
			))
			.ToList();

		return new PagedResult<EventEnvelopeResponse>(envelopes, query.Page, pageSize, total);
	}

	static JsonElement ParsePayload(string? payload) => string.IsNullOrWhiteSpace(payload) ? JsonSerializer.SerializeToElement("null") : JsonSerializer.SerializeToElement(payload);
}
