using Azure;
using Azure.Data.Tables;
using Purview.EventSourcing.AzureStorage;

namespace Purview.EventSourcing.Admin.AzureStorage.Internal;

static class AzureStorageAdminTableHelpers
{
	const string StreamVersionRowKey = "version";

	public static TableServiceClient CreateTableServiceClient(AzureStorageEventStoreOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		TableClientOptions tableOptions = new();
		if (options.TimeoutInSeconds is > 0)
			tableOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(options.TimeoutInSeconds.Value);

		return new TableServiceClient(options.ConnectionString, tableOptions);
	}

	public static TableClient CreateTableClient(TableServiceClient tableServiceClient, string tableName)
	{
		ArgumentNullException.ThrowIfNull(tableServiceClient);
		ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
		return tableServiceClient.GetTableClient(tableName);
	}

	public static async Task<IReadOnlyList<string>> ResolveTableNamesAsync(
		TableServiceClient tableServiceClient,
		AzureStorageEventStoreOptions options,
		string? aggregateType,
		CancellationToken cancellationToken
	)
	{
		ArgumentNullException.ThrowIfNull(tableServiceClient);
		ArgumentNullException.ThrowIfNull(options);

		if (!string.IsNullOrWhiteSpace(aggregateType))
		{
			var normalized = aggregateType.Trim();
			var directCandidates = new[] { $"{options.Table}{normalized}", $"{options.Table}{normalized}Aggregate" };

			List<string> matched = [];
			foreach (var candidate in directCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var exists = await TableExistsAsync(tableServiceClient, candidate, cancellationToken);
				if (exists)
					matched.Add(candidate);
			}

			if (matched.Count > 0)
				return matched;
		}

		List<string> names = [];
		await foreach (
			var table in tableServiceClient.QueryAsync(filter: (string?)null, maxPerPage: 100, cancellationToken)
		)
		{
			if (table.Name.StartsWith(options.Table, StringComparison.OrdinalIgnoreCase))
				names.Add(table.Name);
		}

		return names;
	}

	static async Task<bool> TableExistsAsync(
		TableServiceClient tableServiceClient,
		string tableName,
		CancellationToken cancellationToken
	)
	{
		try
		{
			await tableServiceClient
				.GetTableClient(tableName)
				.GetAccessPoliciesAsync(cancellationToken: cancellationToken);
			return true;
		}
		catch (RequestFailedException ex) when (ex.Status == 404)
		{
			return false;
		}
	}

	public static string BuildEventRangeFilter(
		string aggregateId,
		string eventPrefix,
		int eventSuffixLength,
		long versionFrom,
		long? versionTo
	)
	{
		var fromKey = BuildEventRowKey(eventPrefix, eventSuffixLength, versionFrom);
		var toKey = BuildEventRowKey(eventPrefix, eventSuffixLength, versionTo ?? int.MaxValue);
		return $"(PartitionKey eq '{aggregateId}') and ((RowKey ge '{fromKey}') and (RowKey le '{toKey}'))";
	}

	public static string BuildEventRowKey(string eventPrefix, int eventSuffixLength, long version) =>
		$"{eventPrefix}_{version.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(eventSuffixLength, '0')}";

	public static bool TryParseEventVersion(string rowKey, string eventPrefix, out long version)
	{
		version = default;
		var prefix = $"{eventPrefix}_";
		return rowKey.StartsWith(prefix, StringComparison.Ordinal)
			&& long.TryParse(rowKey[prefix.Length..], out version);
	}

	public static bool MatchesAggregateType(string persistedAggregateType, string requestedAggregateType) =>
		string.Equals(
			persistedAggregateType?.Trim(),
			requestedAggregateType?.Trim(),
			StringComparison.OrdinalIgnoreCase
		);

	public static string BuildAggregateSearchFilter(string? aggregateId = null) =>
		string.IsNullOrWhiteSpace(aggregateId)
			? $"RowKey eq '{StreamVersionRowKey}'"
			: $"(RowKey eq '{StreamVersionRowKey}') and (PartitionKey eq '{aggregateId}')";
}
