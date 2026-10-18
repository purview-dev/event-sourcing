using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Queries;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Admin.Postgres.Internal;
using Purview.EventSourcing.Postgres.Events;
using Purview.EventSourcing.Postgres.Events.EntityFramework;

namespace Purview.EventSourcing.Admin.Postgres;

/// <summary>
/// Provides event range queries against PostgreSQL for the Admin portal.
/// </summary>
/// <remarks>
/// The service resolves the table that holds the requested aggregate type via <c>PostgresAdminTableResolver</c>
/// and translates the version, time and paging filters into Entity Framework Core queries executed against the
/// database.
/// </remarks>
/// <param name="options">The configured <see cref="PostgresEventStoreOptions"/>.</param>
public sealed class PostgresAdminEventQueryService(IOptions<PostgresEventStoreOptions> options)
	: IAdminEventQueryService
{
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

		var table = PostgresAdminTableResolver.ResolveTable(options.Value, aggregateType);
		await using var context = CreateContext(options.Value, table);
		var aggregateTypeFilter = table.AggregateTypeFilter;

		var rows = context
			.EventStoreEntities.AsNoTracking()
			.Where(x =>
				(aggregateTypeFilter == null || x.AggregateType == aggregateTypeFilter)
				&& x.AggregateId == aggregateId
				&& x.EntityType == 1
			);

		if (query.VersionFrom is not null)
			rows = rows.Where(x => x.Version >= query.VersionFrom.Value);

		if (query.VersionTo is not null)
			rows = rows.Where(x => x.Version <= query.VersionTo.Value);

		if (query.TimeFromUtc is not null)
			rows = rows.Where(x => x.Timestamp >= query.TimeFromUtc.Value);

		if (query.TimeToUtc is not null)
			rows = rows.Where(x => x.Timestamp <= query.TimeToUtc.Value);

		var directionDesc = query.Sort.Contains("desc", StringComparison.OrdinalIgnoreCase);
		rows = directionDesc ? rows.OrderByDescending(x => x.Version) : rows.OrderBy(x => x.Version);

		var totalCount = await rows.LongCountAsync(cancellationToken);
		var page = Math.Max(1, query.Page);
		var pageSize = Math.Max(1, query.PageSize);
		if (totalCount == 0)
			return new PagedResult<EventEnvelopeResponse>([], page, pageSize, 0);

		var pageRows = await rows.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

		var items = pageRows
			.Select(row => new EventEnvelopeResponse(
				row.AggregateType,
				row.AggregateId,
				new EventMetadataResponse(
					row.Version,
					row.Timestamp,
					row.EventType ?? string.Empty,
					row.SchemaVersion,
					row.CorrelationId,
					row.CausationId,
					row.IdempotencyId,
					row.UserId
				),
				ParsePayload(row.Payload)
			))
			.ToList();

		return new PagedResult<EventEnvelopeResponse>(items, page, pageSize, totalCount);
	}

	static EventStoreDbContext CreateContext(PostgresEventStoreOptions options, PostgresAdminTableDescriptor table)
	{
		DbContextOptionsBuilder<EventStoreDbContext> builder = new();
		builder.UseNpgsql(options.ConnectionString);
		return new EventStoreDbContext(builder.Options, table.SchemaName, table.TableName);
	}

	static JsonElement ParsePayload(string? payload)
	{
		if (string.IsNullOrWhiteSpace(payload))
			return JsonDocument.Parse("null").RootElement.Clone();

		using var document = JsonDocument.Parse(payload);
		return document.RootElement.Clone();
	}
}
