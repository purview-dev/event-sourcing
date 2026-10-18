using System.Text.Json;
using Azure;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Admin.AzureStorage.Internal;
using Purview.EventSourcing.AzureStorage;
using Purview.EventSourcing.AzureStorage.Entities;

namespace Purview.EventSourcing.Admin.AzureStorage;

/// <summary>
/// Projects aggregate state at a point in time from Azure Table Storage for the Admin portal.
/// </summary>
/// <remarks>
/// The service replays the stored events of an aggregate and produces a <see cref="ProjectionResponse"/> that
/// captures the projected state, the highest version reached and a <see cref="ProjectionProvenance"/> describing
/// which event versions were applied and which were skipped.
/// </remarks>
/// <param name="options">The configured <see cref="AzureStorageEventStoreOptions"/>.</param>
public sealed class AzureStorageAdminProjectionService(IOptions<AzureStorageEventStoreOptions> options)
	: IAdminProjectionService
{
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

		var events = await GetEventsAsync(
			aggregateType,
			aggregateId,
			version => version <= targetVersion,
			cancellationToken
		);

		return BuildProjection(aggregateType, aggregateId, events, $"Events projected up to version {targetVersion}");
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

		var events = await GetEventsAsync(
			aggregateType,
			aggregateId,
			(_, timestamp) => timestamp is not null && timestamp <= targetUtc,
			cancellationToken
		);

		return BuildProjection(aggregateType, aggregateId, events, $"Events projected up to timestamp {targetUtc:O}");
	}

	static ProjectionResponse? BuildProjection(
		string aggregateType,
		string aggregateId,
		List<(long Version, EventEntity Event)> events,
		string reason
	)
	{
		if (events.Count == 0)
			return null;

		List<long> appliedVersions = [];
		List<long> skippedVersions = [];
		Dictionary<string, object> projectedState = [];

		foreach (var item in events.OrderBy(x => x.Version))
		{
			try
			{
				if (item.Event.Payload is not null && item.Event.EventType is not null)
				{
					using var _ = JsonDocument.Parse(item.Event.Payload);
					projectedState[$"event_{item.Version}"] = new
					{
						eventType = item.Event.EventType,
						version = item.Version,
						timestamp = item.Event.Timestamp,
					};
					appliedVersions.Add(item.Version);
				}
				else
				{
					skippedVersions.Add(item.Version);
				}
			}
			catch (JsonException)
			{
				skippedVersions.Add(item.Version);
			}
		}

		var finalState = JsonDocument.Parse(JsonSerializer.Serialize(projectedState)).RootElement.Clone();
		var projectedVersion = events.Max(x => x.Version);
		var projectedAtUtc = events.OrderByDescending(x => x.Version).First().Event.Timestamp;

		return new ProjectionResponse(
			aggregateType,
			aggregateId,
			projectedVersion,
			projectedAtUtc,
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

	async Task<List<(long Version, EventEntity Event)>> GetEventsAsync(
		string aggregateType,
		string aggregateId,
		Func<long, bool> versionPredicate,
		CancellationToken cancellationToken
	) => await GetEventsAsync(aggregateType, aggregateId, (version, _) => versionPredicate(version), cancellationToken);

	async Task<List<(long Version, EventEntity Event)>> GetEventsAsync(
		string aggregateType,
		string aggregateId,
		Func<long, DateTimeOffset?, bool> eventPredicate,
		CancellationToken cancellationToken
	)
	{
		var config = options.Value;
		var tableService = AzureStorageAdminTableHelpers.CreateTableServiceClient(config);
		var tableNames = await AzureStorageAdminTableHelpers.ResolveTableNamesAsync(
			tableService,
			config,
			aggregateType,
			cancellationToken
		);

		List<(long Version, EventEntity Event)> rows = [];
		foreach (var tableName in tableNames)
		{
			var table = AzureStorageAdminTableHelpers.CreateTableClient(tableService, tableName);
			var filter = AzureStorageAdminTableHelpers.BuildEventRangeFilter(
				aggregateId,
				config.EventPrefix,
				config.EventSuffixLength,
				versionFrom: 1,
				versionTo: null
			);

			try
			{
				await foreach (
					var row in table.QueryAsync<EventEntity>(
						filter,
						maxPerPage: 100,
						cancellationToken: cancellationToken
					)
				)
				{
					if (
						!AzureStorageAdminTableHelpers.TryParseEventVersion(
							row.RowKey,
							config.EventPrefix,
							out var version
						)
					)
						continue;

					if (!eventPredicate(version, row.Timestamp))
						continue;

					rows.Add((version, row));
				}
			}
			catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == "TableNotFound")
			{
				continue;
			}
		}

		return rows;
	}
}
