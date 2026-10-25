using System.Text.Json;
using Azure;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.AzureStorage.Internal;
using Purview.EventSourcing.Admin.Failures;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Queries;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.AzureStorage;
using Purview.EventSourcing.AzureStorage.Entities;
using Purview.Results;

namespace Purview.EventSourcing.Admin.AzureStorage;

/// <summary>
/// Provides event range queries against Azure Table Storage for the Admin portal.
/// </summary>
/// <remarks>
/// The service reads the event rows written by the Azure Storage event store and exposes them as
/// <see cref="EventEnvelopeResponse"/> values. Event versions are decoded from each row's
/// <see cref="Azure.Data.Tables.TableEntity.RowKey"/> using the configured event prefix.
/// </remarks>
/// <param name="options">The configured <see cref="AzureStorageEventStoreOptions"/>.</param>
public sealed class AzureStorageAdminEventQueryService(IOptions<AzureStorageEventStoreOptions> options)
	: IAdminEventQueryService
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

		var config = options.Value;
		var tableService = AzureStorageAdminTableHelpers.CreateTableServiceClient(config);
		var tableNames = await AzureStorageAdminTableHelpers.ResolveTableNamesAsync(
			tableService,
			config,
			aggregateType,
			cancellationToken
		);

		List<(string AggregateType, long Version, EventEntity Event)> rows = [];
		foreach (var tableName in tableNames)
		{
			var table = AzureStorageAdminTableHelpers.CreateTableClient(tableService, tableName);
			var filter = AzureStorageAdminTableHelpers.BuildEventRangeFilter(
				aggregateId,
				config.EventPrefix,
				config.EventSuffixLength,
				(int)(query.VersionFrom ?? 1),
				query.VersionTo is null ? null : (int)query.VersionTo.Value
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

					if (query.TimeFromUtc is not null && row.Timestamp < query.TimeFromUtc)
						continue;
					if (query.TimeToUtc is not null && row.Timestamp > query.TimeToUtc)
						continue;

					rows.Add((aggregateType, version, row));
				}
			}
			catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == "TableNotFound")
			{
				continue;
			}
		}

		var page = Math.Max(1, query.Page);
		var pageSize = Math.Max(1, query.PageSize);
		if (rows.Count == 0)
			return new PagedResult<EventEnvelopeResponse>([], page, pageSize, 0);

		var directionDesc = query.Sort.Contains("desc", StringComparison.OrdinalIgnoreCase);
		var ordered = directionDesc
			? [.. rows.OrderByDescending(x => x.Version)]
			: rows.OrderBy(x => x.Version).ToList();

		var totalCount = ordered.Count;
		var pageRows = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

		var items = pageRows
			.Select(item => new EventEnvelopeResponse(
				item.AggregateType,
				item.Event.PartitionKey,
				new EventMetadataResponse(
					item.Version,
					item.Event.Timestamp ?? DateTimeOffset.MinValue,
					item.Event.EventType ?? string.Empty,
					item.Event.SchemaVersion,
					item.Event.CorrelationId,
					item.Event.CausationId,
					item.Event.IdempotencyId,
					item.Event.UserId
				),
				ParsePayload(item.Event.Payload)
			))
			.ToList();

		return new PagedResult<EventEnvelopeResponse>(items, page, pageSize, totalCount);
	}

	static JsonElement ParsePayload(string? payload)
	{
		if (string.IsNullOrWhiteSpace(payload))
			return JsonSerializer.SerializeToElement("null");

		using var document = JsonDocument.Parse(payload);
		return document.RootElement.Clone();
	}
}
