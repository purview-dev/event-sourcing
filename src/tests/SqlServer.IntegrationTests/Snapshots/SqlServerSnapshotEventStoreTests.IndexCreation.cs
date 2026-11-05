using System.Globalization;
using Microsoft.Data.SqlClient;
using Purview.EventSourcing.Aggregates.Persistence;

namespace Purview.EventSourcing.SqlServer.Snapshots;

partial class SQLServerSnapshotEventStoreTests
{
	[Test]
	public async Task SaveAsync_GivenConfiguredJsonIndex_CreatesSnapshotComputedColumnAndIndex(
		CancellationToken cancellationToken
	)
	{
		var runId = Guid.NewGuid();
		var tableName = $"EventStoreSnapshots_{runId:N}";
		const string indexName = "IX_Snapshot_StringProperty";
		const string computedColumnName = "Json_StringProperty";
		var store = fixture.CreateSnapshotStore<PersistenceAggregate>(
			runId: runId,
			configureOptions: options =>
				options.JsonIndexOptions = new SqlServerJsonIndexOptions
				{
					Enabled = true,
					Indexes =
					[
						new SqlServerJsonIndexDefinition
						{
							JsonPath = "$.StringProperty",
							ComputedColumnName = computedColumnName,
							IndexName = indexName,
							IncludeColumns = ["Id"],
						},
					],
				}
		);

		var aggregate = CreateAggregate($"agg_{Guid.NewGuid():N}", static x => x.AppendString("snapshot-json-index"));

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
}
