using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Fixtures.SqlServer;
using Purview.EventSourcing.SqlServer.Snapshot;
using Purview.EventSourcing.SqlServer.Snapshots;

namespace Purview.EventSourcing.SqlServer.Events;

[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed partial class SqlServerEventStoreTransactionIntegrationTests(SqlServerEventStoreFixture fixture)
{
	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "SQL Server transaction integration scenario intentionally touches the full transaction surface."
	)]
	public async Task CommitAsync_GivenEnlistedRawSqlOperation_CommitsAggregateAndRawSqlTogether(
		CancellationToken cancellationToken
	)
	{
		var eventStore = fixture.CreateEventStore<PersistenceAggregate>();
		var aggregateId = $"{Guid.NewGuid():N}";
		var tableName = $"TransactionAudit_{Guid.NewGuid():N}";

		await EnsureAuditTableExistsAsync(fixture.ConnectionString, tableName, cancellationToken);

		var aggregate = await eventStore.CreateAsync(aggregateId, cancellationToken);
		aggregate.AppendString("raw-sql");

		SqlServerEventStoreTransactionFactory factory = new(new FixedCorrelationIdProvider("sql-raw"));
		await using var transaction = factory.CreateSqlServerTransaction();
		transaction.Enlist(aggregate, eventStore);
		transaction.Enlist(
			async (connection, sqlTransaction, token) =>
			{
				var quotedTableName = QuoteTableName(tableName);
#pragma warning disable CA2100
				await using SqlCommand command = new(
					$"INSERT INTO {quotedTableName} ([CorrelationId], [Value]) VALUES (@correlationId, @value)",
					connection,
					sqlTransaction
				);
#pragma warning restore CA2100
				command.Parameters.AddWithValue("@correlationId", "sql-raw");
				command.Parameters.AddWithValue("@value", "raw-sql");
				await command.ExecuteNonQueryAsync(token);
			}
		);

		var result = await transaction.CommitAsync(cancellationToken);
		var savedAggregateResult = await eventStore.GetAsync(aggregateId, null, cancellationToken);
		var savedAggregate = savedAggregateResult.IsSuccess ? savedAggregateResult.Value : null;
		var rowCount = await GetAuditRowCountAsync(fixture.ConnectionString, tableName, cancellationToken);

		await Assert.That(result.Success).IsTrue();
		await Assert.That(savedAggregate).IsNotNull();
		await Assert.That(savedAggregate!.StringProperty).Contains("raw-sql");
		await Assert.That(rowCount).IsEqualTo(1);
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "SQL Server transaction integration scenario intentionally touches the full transaction surface."
	)]
	public async Task CommitAsync_GivenEnlistedEfOperation_CommitsAggregateAndEfSaveTogether(
		CancellationToken cancellationToken
	)
	{
		var eventStore = fixture.CreateEventStore<PersistenceAggregate>();
		var aggregateId = $"{Guid.NewGuid():N}";
		var tableName = $"TransactionAudit_{Guid.NewGuid():N}";

		await EnsureAuditTableExistsAsync(fixture.ConnectionString, tableName, cancellationToken);

		var aggregate = await eventStore.CreateAsync(aggregateId, cancellationToken);
		aggregate.AppendString("ef");

		SqlServerEventStoreTransactionFactory factory = new(new FixedCorrelationIdProvider("sql-ef"));
		await using var transaction = factory.CreateSqlServerTransaction();
		transaction.Enlist(aggregate, eventStore);
		transaction.Enlist(
			connection => new TransactionAuditDBContext(connection, tableName),
			async (dbContext, token) =>
			{
				dbContext.Entries.Add(new TransactionAuditEntry { CorrelationId = "sql-ef", Value = "ef" });
				await dbContext.SaveChangesAsync(token);
			}
		);

		var result = await transaction.CommitAsync(cancellationToken);
		var savedAggregateResult = await eventStore.GetAsync(aggregateId, null, cancellationToken);
		var savedAggregate = savedAggregateResult.IsSuccess ? savedAggregateResult.Value : null;
		var rowCount = await GetAuditRowCountAsync(fixture.ConnectionString, tableName, cancellationToken);

		await Assert.That(result.Success).IsTrue();
		await Assert.That(savedAggregate).IsNotNull();
		await Assert.That(savedAggregate!.StringProperty).Contains("ef");
		await Assert.That(rowCount).IsEqualTo(1);
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "SQL Server transaction integration scenario intentionally touches the full transaction surface."
	)]
	public async Task CommitAsync_GivenEnlistedSqlOperationThrows_RollsBackAggregateAndSqlInsert(
		CancellationToken cancellationToken
	)
	{
		var eventStore = fixture.CreateEventStore<PersistenceAggregate>();
		var aggregateId = $"{Guid.NewGuid():N}";
		var tableName = $"TransactionAudit_{Guid.NewGuid():N}";

		await EnsureAuditTableExistsAsync(fixture.ConnectionString, tableName, cancellationToken);

		var aggregate = await eventStore.CreateAsync(aggregateId, cancellationToken);
		aggregate.AppendString("rollback");

		SqlServerEventStoreTransactionFactory factory = new(new FixedCorrelationIdProvider("sql-rollback"));
		await using var transaction = factory.CreateSqlServerTransaction();
		transaction.Enlist(aggregate, eventStore);
		transaction.Enlist(
			async (connection, sqlTransaction, token) =>
			{
				var quotedTableName = QuoteTableName(tableName);
#pragma warning disable CA2100
				await using SqlCommand command = new(
					$"INSERT INTO {quotedTableName} ([CorrelationId], [Value]) VALUES (@correlationId, @value)",
					connection,
					sqlTransaction
				);
#pragma warning restore CA2100
				command.Parameters.AddWithValue("@correlationId", "sql-rollback");
				command.Parameters.AddWithValue("@value", "rollback");
				await command.ExecuteNonQueryAsync(token);
				throw new InvalidOperationException("forced-failure");
			}
		);

		var result = await transaction.CommitAsync(cancellationToken);
		var savedAggregateResult = await eventStore.GetAsync(aggregateId, null, cancellationToken);
		var savedAggregate = savedAggregateResult.IsSuccess ? savedAggregateResult.Value : null;
		var rowCount = await GetAuditRowCountAsync(fixture.ConnectionString, tableName, cancellationToken);

		await Assert.That(result.Success).IsFalse();
		await Assert.That(result.Results).Count().IsEqualTo(1);
		await Assert.That(result.Results[0].Error).IsTypeOf<InvalidOperationException>();
		await Assert.That(savedAggregate).IsNull();
		await Assert.That(rowCount).IsEqualTo(0);
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "SQL Server transaction integration scenario intentionally touches the full transaction surface."
	)]
	public async Task CommitAsync_GivenMultipleCrossImplementationStoresAndRawSql_CommitsAllInSingleTransaction(
		CancellationToken cancellationToken
	)
	{
		var runId = Guid.NewGuid();
		var primaryStore = fixture.CreateEventStore<PersistenceAggregate>(runId: runId);
		var snapshotStore = CreateSnapshotStore(runId);
		var firstAggregateId = $"{Guid.NewGuid():N}";
		var secondAggregateId = $"{Guid.NewGuid():N}";
		var tableName = $"TransactionAudit_{Guid.NewGuid():N}";

		await EnsureAuditTableExistsAsync(fixture.ConnectionString, tableName, cancellationToken);

		var firstAggregate = await primaryStore.CreateAsync(firstAggregateId, cancellationToken);
		firstAggregate.AppendString("primary");

		var secondAggregate = await snapshotStore.CreateAsync(secondAggregateId, cancellationToken);
		secondAggregate.AppendString("snapshot");

		SqlServerEventStoreTransactionFactory factory = new(new FixedCorrelationIdProvider("sql-cross"));
		await using var transaction = factory.CreateSqlServerTransaction();
		transaction.Enlist(firstAggregate, primaryStore);
		transaction.Enlist(secondAggregate, snapshotStore);
		transaction.Enlist(
			async (connection, sqlTransaction, token) =>
			{
				var quotedTableName = QuoteTableName(tableName);
#pragma warning disable CA2100
				await using SqlCommand command = new(
					$"INSERT INTO {quotedTableName} ([CorrelationId], [Value]) VALUES (@correlationId, @value)",
					connection,
					sqlTransaction
				);
#pragma warning restore CA2100
				command.Parameters.AddWithValue("@correlationId", "sql-cross");
				command.Parameters.AddWithValue("@value", "cross");
				await command.ExecuteNonQueryAsync(token);
			}
		);

		var result = await transaction.CommitAsync(cancellationToken);
		var savedFirstResult = await primaryStore.GetAsync(firstAggregateId, null, cancellationToken);
		var savedFirst = savedFirstResult.IsSuccess ? savedFirstResult.Value : null;
		var savedSecondResult = await snapshotStore.GetAsync(secondAggregateId, null, cancellationToken);
		var savedSecond = savedSecondResult.IsSuccess ? savedSecondResult.Value : null;
		var rowCount = await GetAuditRowCountAsync(fixture.ConnectionString, tableName, cancellationToken);

		await Assert.That(result.Success).IsTrue();
		await Assert.That(savedFirst).IsNotNull();
		await Assert.That(savedFirst!.StringProperty).Contains("primary");
		await Assert.That(savedSecond).IsNotNull();
		await Assert.That(savedSecond!.StringProperty).Contains("snapshot");
		await Assert.That(rowCount).IsEqualTo(1);
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "SQL Server transaction integration scenario intentionally touches the full transaction surface."
	)]
	public async Task CommitAsync_GivenMultipleCrossImplementationStoresAndFailingRawSql_RollsBackAllChanges(
		CancellationToken cancellationToken
	)
	{
		var runId = Guid.NewGuid();
		var primaryStore = fixture.CreateEventStore<PersistenceAggregate>(runId: runId);
		var snapshotStore = CreateSnapshotStore(runId);
		var firstAggregateId = $"{Guid.NewGuid():N}";
		var secondAggregateId = $"{Guid.NewGuid():N}";
		var tableName = $"TransactionAudit_{Guid.NewGuid():N}";

		await EnsureAuditTableExistsAsync(fixture.ConnectionString, tableName, cancellationToken);

		var firstAggregate = await primaryStore.CreateAsync(firstAggregateId, cancellationToken);
		firstAggregate.AppendString("primary-rollback");

		var secondAggregate = await snapshotStore.CreateAsync(secondAggregateId, cancellationToken);
		secondAggregate.AppendString("snapshot-rollback");

		SqlServerEventStoreTransactionFactory factory = new(new FixedCorrelationIdProvider("sql-cross-rollback"));
		await using var transaction = factory.CreateSqlServerTransaction();
		transaction.Enlist(firstAggregate, primaryStore);
		transaction.Enlist(secondAggregate, snapshotStore);
		transaction.Enlist(
			async (connection, sqlTransaction, token) =>
			{
				var quotedTableName = QuoteTableName(tableName);
#pragma warning disable CA2100
				await using SqlCommand command = new(
					$"INSERT INTO {quotedTableName} ([CorrelationId], [Value]) VALUES (@correlationId, @value)",
					connection,
					sqlTransaction
				);
#pragma warning restore CA2100
				command.Parameters.AddWithValue("@correlationId", "sql-cross-rollback");
				command.Parameters.AddWithValue("@value", "cross-rollback");
				await command.ExecuteNonQueryAsync(token);
				throw new InvalidOperationException("forced-cross-failure");
			}
		);

		var result = await transaction.CommitAsync(cancellationToken);
		var savedFirstResult = await primaryStore.GetAsync(firstAggregateId, null, cancellationToken);
		var savedFirst = savedFirstResult.IsSuccess ? savedFirstResult.Value : null;
		var savedSecondResult = await snapshotStore.GetAsync(secondAggregateId, null, cancellationToken);
		var savedSecond = savedSecondResult.IsSuccess ? savedSecondResult.Value : null;
		var rowCount = await GetAuditRowCountAsync(fixture.ConnectionString, tableName, cancellationToken);

		await Assert.That(result.Success).IsFalse();
		await Assert.That(savedFirst).IsNull();
		await Assert.That(savedSecond).IsNull();
		await Assert.That(rowCount).IsEqualTo(0);
	}

	static async Task EnsureAuditTableExistsAsync(
		string connectionString,
		string tableName,
		CancellationToken cancellationToken
	)
	{
		var quotedTableName = QuoteTableName(tableName);

		await using SqlConnection connection = new(connectionString);
		await connection.OpenAsync(cancellationToken);
#pragma warning disable CA2100
		await using SqlCommand command = new(
			$"""
			IF OBJECT_ID(N'{quotedTableName}', N'U') IS NULL
			BEGIN
				CREATE TABLE {quotedTableName}(
					[Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
					[CorrelationId] NVARCHAR(128) NOT NULL,
					[Value] NVARCHAR(128) NOT NULL
				);
			END
			""",
			connection
		);
#pragma warning restore CA2100
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	static async Task<int> GetAuditRowCountAsync(
		string connectionString,
		string tableName,
		CancellationToken cancellationToken
	)
	{
		var quotedTableName = QuoteTableName(tableName);

		await using SqlConnection connection = new(connectionString);
		await connection.OpenAsync(cancellationToken);
#pragma warning disable CA2100
		await using SqlCommand command = new($"SELECT COUNT(1) FROM {quotedTableName}", connection);
#pragma warning restore CA2100
		var count = await command.ExecuteScalarAsync(cancellationToken);
		return Convert.ToInt32(count, CultureInfo.InvariantCulture);
	}

	static string QuoteTableName(string tableName)
	{
		return AzureTableNameRegEx().IsMatch(tableName)
			? $"[dbo].[{tableName}]"
			: throw new ArgumentException("Audit table name contains unsupported characters.", nameof(tableName));
	}

	sealed class FixedCorrelationIdProvider(string correlationId) : IEventStoreCorrelationIdProvider
	{
		public string? GetCorrelationId() => correlationId;
	}

	sealed class TransactionAuditDBContext(SqlConnection connection, string tableName) : DbContext
	{
		readonly string _tableName = tableName;

		public DbSet<TransactionAuditEntry> Entries => Set<TransactionAuditEntry>();

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
			optionsBuilder.UseSqlServer(connection);

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<TransactionAuditEntry>(entity =>
			{
				entity.ToTable(_tableName, "dbo");
				entity.HasKey(x => x.Id);
				entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
				entity.Property(x => x.Value).HasMaxLength(128).IsRequired();
			});
		}
	}

	sealed class TransactionAuditEntry
	{
		public int Id { get; set; }
		public string CorrelationId { get; set; } = string.Empty;
		public string Value { get; set; } = string.Empty;
	}

	[GeneratedRegex("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
	private static partial Regex AzureTableNameRegEx();

	SqlServerSnapshotEventStore<PersistenceAggregate> CreateSnapshotStore(Guid runId)
	{
		var backingStore = fixture.CreateEventStore<PersistenceAggregate>(runId: runId);
		SqlServerSnapshotEventStoreOptions options = new()
		{
			ConnectionString = fixture.ConnectionString,
			TableName = $"EventStoreSnapshots_{runId:N}",
			SchemaName = "dbo",
			AutoCreateTable = true,
		};

		return new SqlServerSnapshotEventStore<PersistenceAggregate>(
			backingStore,
			Options.Create(options),
			TestHelpers.CreateSqlServerSnapshotEventStoreTelemetry()
		);
	}
}
