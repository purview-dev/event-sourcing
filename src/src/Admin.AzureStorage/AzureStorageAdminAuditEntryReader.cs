using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.AzureStorage;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Admin.AzureStorage;

/// <summary>
/// Reads the Azure Storage table written by <see cref="AzureStorageAdminAuditLogger"/>.
/// </summary>
/// <remarks>
/// <para>
/// A tenant-scoped read names exactly one partition, so another tenant's entries are not filtered
/// out - they are never fetched. The unowned partition is addressable only by
/// <see cref="TenantScope.System"/>.
/// </para>
/// <para>
/// <strong>Known limitation: a system-scope read is more expensive than a tenant-scoped one.</strong>
/// Table Storage orders by partition key then row key, so a cross-partition read is not globally
/// newest-first and this reader sorts the fetched window in memory. Bound such a read with
/// <see cref="AdminAuditQuery.FromUtc"/>, which the inverted row key turns into a server-side range.
/// A tenant-scoped read needs none of that: its single partition is already in time order.
/// </para>
/// </remarks>
/// <param name="auditOptions">The audit options, which name the table.</param>
/// <param name="eventStoreOptions">The event-store options, used for any value not overridden.</param>
/// <param name="currentTenant">Supplies the scope every read is partitioned on.</param>
public sealed class AzureStorageAdminAuditEntryReader(
	IOptions<AzureStorageAdminAuditLoggerOptions> auditOptions,
	IOptions<AzureStorageEventStoreOptions> eventStoreOptions,
	ICurrentTenant currentTenant
) : IAdminAuditEntryReader
{
	/// <inheritdoc/>
	public async Task<AdminAuditPage> QueryAsync(AdminAuditQuery query, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(auditOptions);
		ArgumentNullException.ThrowIfNull(eventStoreOptions);

		var page = Math.Max(1, query.Page);
		var pageSize = Math.Clamp(query.PageSize, 1, AdminAuditQuery.MaximumPageSize);
		var table = AzureStorageAdminAuditLogger.CreateTableClient(auditOptions.Value, eventStoreOptions.Value);

		var scope = currentTenant.Current;
		var filter = BuildFilter(query, scope);

		// Fetched, then narrowed: Table Storage cannot express every predicate server-side, and the
		// window has to cover the requested page plus one to answer HasMore.
		var window = (page * pageSize) + 1;

		List<AdminAuditEntry> matched = [];
		await foreach (var row in table.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken))
		{
			var entry = ToEntry(row);
			if (!Matches(entry, query))
				continue;

			matched.Add(entry);
			if (scope.IsSystem)
				continue;

			// A single partition arrives newest-first already, so the window can stop early. The
			// system scope spans partitions and has to be sorted, so it cannot.
			if (matched.Count >= window)
				break;
		}

		var ordered = scope.IsSystem ? matched.OrderByDescending(entry => entry.TimestampUtc).ToList() : matched;
		var skipped = ordered.Skip((page - 1) * pageSize).Take(pageSize + 1).ToList();

		var hasMore = skipped.Count > pageSize;
		if (hasMore)
			skipped.RemoveAt(skipped.Count - 1);

		return new AdminAuditPage(skipped, page, pageSize, hasMore);
	}

	static string BuildFilter(AdminAuditQuery query, TenantScope scope)
	{
		List<string> clauses = [];

		if (!scope.IsSystem)
			// The partition is the isolation. Nothing else in this method may widen it.
			clauses.Add(
				TableClient.CreateQueryFilter(
					$"PartitionKey eq {AzureStorageAdminAuditKeys.PartitionFor(scope.TenantId)}"
				)
			);

		// Inverted row keys reverse the comparison: a later time is a smaller key, so FromUtc is the
		// upper bound and ToUtc the lower one.
		if (query.FromUtc is not null)
			clauses.Add(
				TableClient.CreateQueryFilter(
					$"RowKey le {AzureStorageAdminAuditKeys.RowKeyBoundFor(query.FromUtc.Value) + "~"}"
				)
			);

		if (query.ToUtc is not null)
			clauses.Add(
				TableClient.CreateQueryFilter(
					$"RowKey ge {AzureStorageAdminAuditKeys.RowKeyBoundFor(query.ToUtc.Value)}"
				)
			);

		if (query.Succeeded is not null)
			clauses.Add(TableClient.CreateQueryFilter($"Succeeded eq {query.Succeeded.Value}"));

		if (query.Feature is not null)
			clauses.Add(TableClient.CreateQueryFilter($"Feature eq {query.Feature.Value.ToString()}"));

		return string.Join(" and ", clauses);
	}

	// Applied after the fetch: these are equality predicates Table Storage would answer with a scan
	// anyway, and keeping them here keeps the server-side filter to the partition and row-key range
	// that the storage layout actually makes cheap.
	static bool Matches(AdminAuditEntry entry, AdminAuditQuery query) =>
		(query.Principal is null || string.Equals(entry.Principal, query.Principal, StringComparison.Ordinal))
		&& (query.Target is null || string.Equals(entry.Target, query.Target, StringComparison.Ordinal))
		&& (
			query.CorrelationId is null
			|| string.Equals(entry.CorrelationId, query.CorrelationId, StringComparison.Ordinal)
		);

	static AdminAuditEntry ToEntry(TableEntity row) =>
		new(
			row.GetDateTimeOffset("TimestampUtc") ?? row.Timestamp ?? DateTimeOffset.MinValue,
			Enum.TryParse<AdminFeature>(row.GetString("Feature"), out var feature) ? feature : default,
			row.GetString("Action") ?? string.Empty,
			row.GetString("Principal"),
			row.GetString("Target"),
			row.GetBoolean("Succeeded") ?? false,
			row.GetString("Details")
		)
		{
			TenantId = row.GetString("TenantId"),
			CorrelationId = row.GetString("CorrelationId"),
		};
}
