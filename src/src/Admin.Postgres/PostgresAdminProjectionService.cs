using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Postgres.Internal;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Postgres.Events;
using Purview.EventSourcing.Postgres.Events.EntityFramework;
using System.Text.Json;

namespace Purview.EventSourcing.Admin.Postgres;

/// <summary>
/// Projects aggregate state at a point in time from PostgreSQL for the Admin portal.
/// </summary>
/// <remarks>
/// The service replays the stored events of an aggregate and produces a <see cref="ProjectionResponse"/> that
/// captures the projected state, the highest version reached and a <see cref="ProjectionProvenance"/> describing
/// which event versions were applied and which were skipped.
/// </remarks>
/// <param name="options">The configured <see cref="PostgresEventStoreOptions"/>.</param>
public sealed class PostgresAdminProjectionService(IOptions<PostgresEventStoreOptions> options)
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

		var table = PostgresAdminTableResolver.ResolveTable(options.Value, aggregateType);
		await using var context = CreateContext(options.Value, table);

		List<long> appliedVersions = [];
		List<long> skippedVersions = [];
		Dictionary<string, object> projectedState = [];

		var rows = context
			.EventStoreEntities.AsNoTracking()
			.Where(x =>
				x.AggregateType == aggregateType
				&& x.AggregateId == aggregateId
				&& x.EntityType == 1
				&& x.Version <= targetVersion
			)
			.OrderBy(x => x.Version);

		var rowList = await rows.ToListAsync(cancellationToken);

		foreach (var row in rowList)
		{
			try
			{
				if (row.Payload != null && row.EventType != null)
				{
					using var _ = JsonDocument.Parse(row.Payload);
					projectedState[$"event_{row.Version}"] = new { eventType = row.EventType, version = row.Version };
					appliedVersions.Add(row.Version);
				}
				else
				{
					skippedVersions.Add(row.Version);
				}
			}
			catch (JsonException)
			{
				skippedVersions.Add(row.Version);
			}
		}

		if (rowList.Count == 0)
			return null;

		var reason =
			targetVersion > rowList.Last().Version
				? $"Events projected up to available version {rowList.Last().Version} (target was {targetVersion})"
				: $"Events projected up to version {targetVersion}";

		var finalState = JsonSerializer.SerializeToElement(projectedState);

		return new ProjectionResponse(
			aggregateType,
			aggregateId,
			rowList.Last().Version,
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

		var table = PostgresAdminTableResolver.ResolveTable(options.Value, aggregateType);
		await using var context = CreateContext(options.Value, table);

		List<long> appliedVersions = [];
		List<long> skippedVersions = [];
		Dictionary<string, object> projectedState = [];

		var rows = context
			.EventStoreEntities.AsNoTracking()
			.Where(x =>
				x.AggregateType == aggregateType
				&& x.AggregateId == aggregateId
				&& x.EntityType == 1
				&& x.Timestamp <= targetUtc
			)
			.OrderBy(x => x.Version);

		var rowList = await rows.ToListAsync(cancellationToken);

		foreach (var row in rowList)
		{
			try
			{
				if (row.Payload != null && row.EventType != null)
				{
					using var _ = JsonDocument.Parse(row.Payload);
					projectedState[$"event_{row.Version}"] = new
					{
						eventType = row.EventType,
						version = row.Version,
						timestamp = row.Timestamp,
					};
					appliedVersions.Add(row.Version);
				}
				else
				{
					skippedVersions.Add(row.Version);
				}
			}
			catch (JsonException)
			{
				skippedVersions.Add(row.Version);
			}
		}

		if (rowList.Count == 0)
			return null;

		var latestTimestamp = rowList.Last().Timestamp;
		var reason =
			latestTimestamp > targetUtc
				? $"Events projected up to available timestamp {latestTimestamp:O} (target was {targetUtc:O})"
				: $"Events projected up to timestamp {targetUtc:O}";

		var finalState = JsonSerializer.SerializeToElement(projectedState);

		return new ProjectionResponse(
			aggregateType,
			aggregateId,
			rowList.Last().Version,
			latestTimestamp,
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

	static EventStoreDbContext CreateContext(PostgresEventStoreOptions options, PostgresAdminTableDescriptor table)
	{
		DbContextOptionsBuilder<EventStoreDbContext> builder = new();
		builder.UseNpgsql(options.ConnectionString);
		return new EventStoreDbContext(builder.Options, table.SchemaName, table.TableName);
	}
}
