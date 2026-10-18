using Azure;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Queries;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Admin.AzureStorage.Internal;
using Purview.EventSourcing.AzureStorage;
using Purview.EventSourcing.AzureStorage.Entities;

namespace Purview.EventSourcing.Admin.AzureStorage;

/// <summary>
/// Provides aggregate summary queries against Azure Table Storage for the Admin portal.
/// </summary>
/// <remarks>
/// The service reads the stream-version rows written by the Azure Storage event store and exposes them as
/// <see cref="AggregateSummaryResponse"/> values. Rows are fetched from every table that could hold the requested
/// aggregate type and are filtered, sorted and paged in memory.
/// </remarks>
/// <param name="options">The configured <see cref="AzureStorageEventStoreOptions"/>.</param>
public sealed class AzureStorageAdminAggregateQueryService(IOptions<AzureStorageEventStoreOptions> options)
	: IAdminAggregateQueryService
{
	///<inheritdoc/>
	public async Task<PagedResult<AggregateSummaryResponse>> SearchAsync(
		AggregateSearchQuery query,
		CancellationToken cancellationToken
	)
	{
		ArgumentNullException.ThrowIfNull(query);

		var page = Math.Max(1, query.Page);
		var pageSize = Math.Max(1, query.PageSize);
		var config = options.Value;
		var tableService = AzureStorageAdminTableHelpers.CreateTableServiceClient(config);
		var tableNames = await AzureStorageAdminTableHelpers.ResolveTableNamesAsync(
			tableService,
			config,
			query.AggregateType,
			cancellationToken
		);

		List<AggregateSummaryResponse> candidates = [];
		foreach (var tableName in tableNames)
		{
			var table = AzureStorageAdminTableHelpers.CreateTableClient(tableService, tableName);
			var filter = AzureStorageAdminTableHelpers.BuildAggregateSearchFilter(query.AggregateId);
			try
			{
				await foreach (
					var row in table.QueryAsync<StreamVersionEntity>(
						filter,
						maxPerPage: 100,
						cancellationToken: cancellationToken
					)
				)
				{
					var aggregateType = row.AggregateType;
					if (!string.IsNullOrWhiteSpace(query.AggregateType))
					{
						if (!AzureStorageAdminTableHelpers.MatchesAggregateType(aggregateType, query.AggregateType))
							continue;
					}

					if (
						!string.IsNullOrWhiteSpace(query.AggregateId)
						&& !string.Equals(row.PartitionKey, query.AggregateId, StringComparison.Ordinal)
					)
						continue;

					var rowTime = row.Timestamp ?? DateTimeOffset.MinValue;

					if (query.FromUtc is not null && rowTime < query.FromUtc.Value)
						continue;
					if (query.ToUtc is not null && rowTime > query.ToUtc.Value)
						continue;
					if (query.IsDeleted is not null && row.IsDeleted != query.IsDeleted.Value)
						continue;
					if (query.IsRestored == true && row.IsDeleted)
						continue;

					candidates.Add(
						new AggregateSummaryResponse(
							aggregateType,
							row.PartitionKey,
							row.Version,
							rowTime,
							rowTime,
							row.IsDeleted,
							!row.IsDeleted
						)
					);
				}
			}
			catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == "TableNotFound")
			{
				continue;
			}
		}

		var ordered = ApplySort(candidates, query.Sort).ToList();
		var totalCount = ordered.Count;
		var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

		return new PagedResult<AggregateSummaryResponse>(pageItems, page, pageSize, totalCount);
	}

	///<inheritdoc/>
	public async Task<AggregateSummaryResponse?> GetAsync(
		string aggregateType,
		string aggregateId,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);

		AggregateSearchQuery query = new(aggregateType, aggregateId, null, null, null, null, 1, 1);
		var result = await SearchAsync(query, cancellationToken);
		return result.Items.Count == 0 ? null : result.Items[0];
	}

	static IEnumerable<AggregateSummaryResponse> ApplySort(IEnumerable<AggregateSummaryResponse> rows, string sort)
	{
		var descending = sort.Contains("desc", StringComparison.OrdinalIgnoreCase);

		return sort switch
		{
			"AggregateId asc" => rows.OrderBy(x => x.AggregateId),
			"AggregateId desc" => rows.OrderByDescending(x => x.AggregateId),
			"CurrentVersion asc" => rows.OrderBy(x => x.CurrentVersion),
			"CurrentVersion desc" => rows.OrderByDescending(x => x.CurrentVersion),
			"CreatedUtc asc" => descending
				? rows.OrderByDescending(x => x.CreatedUtc)
				: rows.OrderBy(x => x.CreatedUtc),
			"CreatedUtc desc" => rows.OrderByDescending(x => x.CreatedUtc),
			_ => descending
				? rows.OrderByDescending(x => x.LastUpdatedUtc).ThenByDescending(x => x.AggregateId)
				: rows.OrderBy(x => x.LastUpdatedUtc).ThenBy(x => x.AggregateId),
		};
	}
}
