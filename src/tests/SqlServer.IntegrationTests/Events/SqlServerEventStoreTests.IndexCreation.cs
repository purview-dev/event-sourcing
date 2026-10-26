using System.Globalization;
using Microsoft.Data.SqlClient;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Fixtures.SqlServer;

namespace Purview.EventSourcing.SqlServer.Events;

[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class SqlServerEventStoreIndexCreationTests(SqlServerEventStoreFixture fixture)
{
	[Test]
	public async Task SaveAsync_GivenDefaultIndexes_CreatesCoreIndexesWithQueryAlignedKeyColumns(
		CancellationToken cancellationToken
	)
	{
		var runId = Guid.NewGuid();
		var tableName = $"EventStoreEvents_{runId:N}";
		var aggregateLookupIndexName = $"IX_{tableName}_AggregateId_EntityType";
		var eventRangeIndexName = $"IX_{tableName}_EventRange";

		var store = fixture.CreateEventStore<PersistenceAggregate>(runId: runId);
		var aggregate = await store.CreateAsync($"agg_{Guid.NewGuid():N}", cancellationToken);
		aggregate.AppendString("core-index-shape");

		var saved = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

		await Assert.That(saved.IsSuccess && saved.Value.Written).IsTrue();

		var aggregateLookupKeyColumns = await GetIndexKeyColumnsAsync(
			fixture.ConnectionString,
			tableName,
			aggregateLookupIndexName,
			cancellationToken
		);
		var eventRangeKeyColumns = await GetIndexKeyColumnsAsync(
			fixture.ConnectionString,
			tableName,
			eventRangeIndexName,
			cancellationToken
		);
		var eventRangeIncludeColumns = await GetIndexIncludeColumnsAsync(
			fixture.ConnectionString,
			tableName,
			eventRangeIndexName,
			cancellationToken
		);

		await Assert
			.That(string.Join(',', aggregateLookupKeyColumns))
			.IsEqualTo("AggregateId,AggregateType,EntityType");
		await Assert.That(string.Join(',', eventRangeKeyColumns)).IsEqualTo("AggregateId,AggregateType,Version");
		await Assert
			.That(string.Join(',', eventRangeIncludeColumns))
			.IsEqualTo("Payload,EventType,IdempotencyId,SchemaVersion,CorrelationId,CausationId,UserId,Timestamp");
	}

	[Test]
	public async Task SaveAsync_GivenConfiguredJsonIndex_CreatesEventComputedColumnAndIndex(
		CancellationToken cancellationToken
	)
	{
		var runId = Guid.NewGuid();
		var tableName = $"EventStoreEvents_{runId:N}";
		const string indexName = "IX_Event_Value";
		const string computedColumnName = "Json_Value";
		var store = fixture.CreateEventStore<PersistenceAggregate>(
			runId: runId,
			configureOptions: options =>
				options.JsonIndexOptions = new SqlServerJsonIndexOptions
				{
					Enabled = true,
					Indexes =
					[
						new SqlServerJsonIndexDefinition
						{
							JsonPath = "$.Value",
							ComputedColumnName = computedColumnName,
							IndexName = indexName,
							IncludeColumns = ["AggregateId", "Version"],
							Filter = "[EntityType] = 1",
						},
					],
				}
		);

		var aggregate = await store.CreateAsync($"agg_{Guid.NewGuid():N}", cancellationToken);
		aggregate.AppendString("event-json-index");

		var saved = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

		await Assert.That(saved.IsSuccess && saved.Value.Written).IsTrue();
		await Assert
			.That(await ColumnExistsAsync(fixture.ConnectionString, tableName, computedColumnName, cancellationToken))
			.IsTrue();
		await Assert
			.That(await IndexExistsAsync(fixture.ConnectionString, tableName, indexName, cancellationToken))
			.IsTrue();
	}

	static async Task<bool> ColumnExistsAsync(
		string connectionString,
		string tableName,
		string columnName,
		CancellationToken cancellationToken
	)
	{
		await using SqlConnection connection = new(connectionString);
		await connection.OpenAsync(cancellationToken);
		await using SqlCommand command = new(
			"""
			SELECT COUNT(1)
			FROM sys.columns c
			INNER JOIN sys.tables t ON t.object_id = c.object_id
			INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
			WHERE s.name = 'dbo' AND t.name = @tableName AND c.name = @columnName;
			""",
			connection
		);
		command.Parameters.AddWithValue("@tableName", tableName);
		command.Parameters.AddWithValue("@columnName", columnName);
		return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
	}

	static async Task<bool> IndexExistsAsync(
		string connectionString,
		string tableName,
		string indexName,
		CancellationToken cancellationToken
	)
	{
		await using SqlConnection connection = new(connectionString);
		await connection.OpenAsync(cancellationToken);
		await using SqlCommand command = new(
			"""
			SELECT COUNT(1)
			FROM sys.indexes i
			INNER JOIN sys.tables t ON t.object_id = i.object_id
			INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
			WHERE s.name = 'dbo' AND t.name = @tableName AND i.name = @indexName;
			""",
			connection
		);
		command.Parameters.AddWithValue("@tableName", tableName);
		command.Parameters.AddWithValue("@indexName", indexName);
		return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
	}

	static async Task<List<string>> GetIndexKeyColumnsAsync(
		string connectionString,
		string tableName,
		string indexName,
		CancellationToken cancellationToken
	)
	{
		await using SqlConnection connection = new(connectionString);
		await connection.OpenAsync(cancellationToken);
		await using SqlCommand command = new(
			"""
			SELECT c.name
			FROM sys.indexes i
			INNER JOIN sys.tables t ON t.object_id = i.object_id
			INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
			INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
			INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
			WHERE s.name = 'dbo'
			  AND t.name = @tableName
			  AND i.name = @indexName
			  AND ic.is_included_column = 0
			ORDER BY ic.key_ordinal;
			""",
			connection
		);
		command.Parameters.AddWithValue("@tableName", tableName);
		command.Parameters.AddWithValue("@indexName", indexName);

		List<string> result = [];
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
			result.Add(reader.GetString(0));

		return result;
	}

	static async Task<List<string>> GetIndexIncludeColumnsAsync(
		string connectionString,
		string tableName,
		string indexName,
		CancellationToken cancellationToken
	)
	{
		await using SqlConnection connection = new(connectionString);
		await connection.OpenAsync(cancellationToken);
		await using SqlCommand command = new(
			"""
			SELECT c.name
			FROM sys.indexes i
			INNER JOIN sys.tables t ON t.object_id = i.object_id
			INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
			INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
			INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
			WHERE s.name = 'dbo'
			  AND t.name = @tableName
			  AND i.name = @indexName
			  AND ic.is_included_column = 1
			ORDER BY ic.index_column_id;
			""",
			connection
		);
		command.Parameters.AddWithValue("@tableName", tableName);
		command.Parameters.AddWithValue("@indexName", indexName);

		List<string> result = [];
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
			result.Add(reader.GetString(0));

		return result;
	}
}
