using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Failures;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Queries;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Admin.SQLServer.Internal;
using Purview.EventSourcing.SqlServer.Events;
using Purview.EventSourcing.SqlServer.Events.EntityFramework;
using Purview.EventSourcing.Tenancy;
using Purview.Results;

namespace Purview.EventSourcing.Admin.SQLServer;

/// <summary>
/// Provides event range queries against SQL Server for the Admin portal.
/// </summary>
/// <remarks>
/// The service resolves the table that holds the requested aggregate type via <c>SqlServerAdminTableResolver</c>
/// and translates the version, time and paging filters into Entity Framework Core queries executed against the
/// database.
/// </remarks>
/// <param name="options">The configured <see cref="SqlServerEventStoreOptions"/>.</param>
/// <param name="currentTenant">Optional tenant scope; when omitted the query is not tenant-filtered.</param>
public sealed class SqlServerAdminEventQueryService(
	IOptions<SqlServerEventStoreOptions> options,
	ICurrentTenant? currentTenant = null
) : IAdminEventQueryService
{
	///<inheritdoc/>
	public async Task<Result<PagedResult<EventEnvelopeResponse>, AdminQueryFailure>> GetRangeAsync(
		string aggregateType,
		string aggregateId,
		EventRangeQuery query,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
		ArgumentNullException.ThrowIfNull(query);

		var table = SqlServerAdminTableResolver.ResolveTable(options.Value, aggregateType);
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

	EventStoreDbContext CreateContext(SqlServerEventStoreOptions options, SqlServerAdminTableDescriptor table)
	{
		DbContextOptionsBuilder<EventStoreDbContext> builder = new();
		builder.UseSqlServer(options.ConnectionString);
		return new EventStoreDbContext(builder.Options, table.SchemaName, table.TableName, currentTenant);
	}

	static JsonElement ParsePayload(string? payload)
	{
		if (string.IsNullOrWhiteSpace(payload))
			return JsonSerializer.SerializeToElement("null");

		using var document = JsonDocument.Parse(payload);
		return document.RootElement.Clone();
	}
}
