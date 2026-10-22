using MongoDB.Driver;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.MongoDB.Events.Entities;
using System.Text.Json;

namespace Purview.EventSourcing.Admin.MongoDB;

/// <summary>
/// Projects aggregate state at a point in time from MongoDB for the Admin portal.
/// </summary>
/// <remarks>
/// The service replays the stored events of an aggregate from the <c>es-{aggregateType}-events</c> collection and
/// produces a <see cref="ProjectionResponse"/> that captures the projected state, the highest version reached and
/// a <see cref="ProjectionProvenance"/> describing which event versions were applied and which were skipped.
/// </remarks>
/// <param name="mongoClient">The MongoDB client used to reach the event store database.</param>
/// <param name="databaseName">The name of the database that holds the event store collections.</param>
public sealed class MongoDBAdminProjectionService(IMongoClient mongoClient, string databaseName)
	: IAdminProjectionService
{
	readonly IMongoClient _mongoClient = mongoClient ?? throw new ArgumentNullException(nameof(mongoClient));
	readonly string _databaseName = databaseName ?? throw new ArgumentNullException(nameof(databaseName));

	///<inheritdoc/>
	public async Task<ProjectionResponse?> ProjectAtVersionAsync(
		string aggregateType,
		string aggregateId,
		long targetVersion,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
		if (targetVersion < 1)
			throw new ArgumentOutOfRangeException(nameof(targetVersion), "Target version must be >= 1");

		var database = _mongoClient.GetDatabase(_databaseName);
		var collectionName = $"es-{aggregateType}-events";
		var collection = database.GetCollection<EventEntity>(collectionName);

		var filter = Builders<EventEntity>.Filter.And(
			Builders<EventEntity>.Filter.Eq(x => x.AggregateId, aggregateId),
			Builders<EventEntity>.Filter.Eq(x => x.EntityType, EntityTypes.EventType),
			Builders<EventEntity>.Filter.Lte(x => x.Version, targetVersion)
		);

		var events = await collection.Find(filter).SortBy(x => x.Version).ToListAsync(cancellationToken);

		if (events.Count == 0)
		{
			return null;
		}

		List<long> appliedVersions = [];
		List<long> skippedVersions = [];
		Dictionary<string, object> projectedState = [];

		foreach (var evt in events)
		{
			try
			{
				if (!string.IsNullOrEmpty(evt.EventType) && !string.IsNullOrEmpty(evt.Payload))
				{
					projectedState[$"event_{evt.Version}"] = new { eventType = evt.EventType, version = evt.Version };
					appliedVersions.Add(evt.Version);
				}
				else
				{
					skippedVersions.Add(evt.Version);
				}
			}
			catch (JsonException)
			{
				// Skip malformed payloads so one bad event does not fail the entire projection.
				skippedVersions.Add(evt.Version);
			}
		}

		var lastEvent = events.Last();
		var reason =
			lastEvent.Version < targetVersion
				? $"Events projected up to available version {lastEvent.Version} (target was {targetVersion})"
				: $"Events projected up to version {targetVersion}";

		var finalState = JsonSerializer.SerializeToElement(projectedState);
		return new ProjectionResponse(
			aggregateType,
			aggregateId,
			lastEvent.Version,
			null,
			finalState,
			new ProjectionProvenance(
				appliedVersions.Count,
				skippedVersions.Count,
				appliedVersions.AsReadOnly(),
				skippedVersions.AsReadOnly(),
				reason
			)
		);
	}

	///<inheritdoc/>
	public async Task<ProjectionResponse?> ProjectAtTimeAsync(
		string aggregateType,
		string aggregateId,
		DateTimeOffset targetUtc,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);

		var database = _mongoClient.GetDatabase(_databaseName);
		var collectionName = $"es-{aggregateType}-events";
		var collection = database.GetCollection<EventEntity>(collectionName);

		var filter = Builders<EventEntity>.Filter.And(
			Builders<EventEntity>.Filter.Eq(x => x.AggregateId, aggregateId),
			Builders<EventEntity>.Filter.Eq(x => x.EntityType, EntityTypes.EventType),
			Builders<EventEntity>.Filter.Lte(x => x.Timestamp, targetUtc)
		);

		var events = await collection.Find(filter).SortBy(x => x.Version).ToListAsync(cancellationToken);

		if (events.Count == 0)
		{
			return null;
		}

		List<long> appliedVersions = [];
		List<long> skippedVersions = [];
		Dictionary<string, object> projectedState = [];

		foreach (var evt in events)
		{
			try
			{
				if (!string.IsNullOrEmpty(evt.EventType) && !string.IsNullOrEmpty(evt.Payload))
				{
					projectedState[$"event_{evt.Version}"] = new
					{
						eventType = evt.EventType,
						version = evt.Version,
						timestamp = evt.Timestamp,
					};
					appliedVersions.Add(evt.Version);
				}
				else
				{
					skippedVersions.Add(evt.Version);
				}
			}
			catch (JsonException)
			{
				// Skip malformed payloads so one bad event does not fail the entire projection.
				skippedVersions.Add(evt.Version);
			}
		}

		var lastEvent = events.Last();
		var reason =
			lastEvent.Timestamp > targetUtc
				? $"Events projected up to available timestamp {lastEvent.Timestamp:O} (target was {targetUtc:O})"
				: $"Events projected up to timestamp {targetUtc:O}";

		var finalState = JsonSerializer.SerializeToElement(projectedState);

		return new ProjectionResponse(
			aggregateType,
			aggregateId,
			lastEvent.Version,
			lastEvent.Timestamp,
			finalState,
			new ProjectionProvenance(
				appliedVersions.Count,
				skippedVersions.Count,
				appliedVersions.AsReadOnly(),
				skippedVersions.AsReadOnly(),
				reason
			)
		);
	}
}
