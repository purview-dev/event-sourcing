using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Outbox;
using Purview.EventSourcing.SqlServer.Events;

namespace Purview.EventSourcing.SqlServer.Outbox;

// SQL strings are built from validated identifiers at construction time, not from user input.
#pragma warning disable CA2100

/// <summary>
/// SQL Server outbox store. Messages are leased atomically with <c>UPDATE ... OUTPUT</c>, ordered by
/// creation time, and deduplicated on <see cref="OutboxEnvelope.IdempotencyKey"/>.
/// </summary>
public sealed partial class SqlServerOutboxStore(
	IOptions<SqlServerOutboxStoreOptions> outboxOptions,
	IOptions<SqlServerEventStoreOptions> eventStoreOptions,
	ILogger<SqlServerOutboxStore> logger
) : IOutboxStore
{
	[GeneratedRegex("^[\\w\\-.]+$", RegexOptions.Compiled)]
	private static partial Regex ValidIdentifier();

	readonly string _connectionString = ResolveConnectionString(outboxOptions.Value, eventStoreOptions.Value);
	readonly string _schema = QuoteIdentifier(outboxOptions.Value.SchemaName);
	readonly string _table = QuoteIdentifier(outboxOptions.Value.TableName);
	bool _schemaEnsured;

	/// <inheritdoc/>
	public Task<int> EnqueueAsync(OutboxEnvelope message, CancellationToken cancellationToken) =>
		ExecuteWithConnectionAsync(
			async (connection, token) =>
			{
				await EnsureSchemaAsync(token);
				return await EnqueueCoreAsync(connection, null, message, token);
			},
			cancellationToken
		);

	/// <summary>
	/// Enqueues a message inside an existing SQL Server transaction, allowing outbox persistence to
	/// share the event save boundary atomically.
	/// </summary>
	public async Task EnqueueInTransactionAsync(
		DbConnection connection,
		DbTransaction transaction,
		OutboxEnvelope message,
		CancellationToken cancellationToken
	)
	{
		ArgumentNullException.ThrowIfNull(connection);
		ArgumentNullException.ThrowIfNull(transaction);
		ArgumentNullException.ThrowIfNull(message);

		await EnsureSchemaAsync(cancellationToken);
		var inserted = await EnqueueCoreAsync(connection, transaction, message, cancellationToken);
		if (inserted > 0)
			LogEnqueued(logger, message.Id);
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<OutboxEnvelope>> ClaimNextBatchAsync(
		string leaseOwner,
		DateTimeOffset leaseUntil,
		int batchSize,
		CancellationToken cancellationToken
	) =>
		await ExecuteWithConnectionAsync(
			async (connection, token) =>
			{
				await EnsureSchemaAsync(token);

				// Without READPAST a concurrent claim blocks on the same rows, then re-applies the
				// outer UPDATE once the first commits - handing both dispatchers the same batch.
				await using SqlCommand command = new(
					$"""
					UPDATE {_schema}.{_table}
					SET [LeaseOwner] = @owner, [LeaseExpiresUtc] = @leaseUntil
					OUTPUT INSERTED.[Id], INSERTED.[AggregateType], INSERTED.[AggregateId], INSERTED.[EventType],
						INSERTED.[PayloadJson], INSERTED.[IdempotencyKey], INSERTED.[CorrelationId], INSERTED.[CreatedUtc],
						INSERTED.[State], INSERTED.[AttemptCount], INSERTED.[NextAttemptUtc], INSERTED.[DispatchedUtc],
						INSERTED.[LeaseExpiresUtc], INSERTED.[LastError]
					WHERE [Id] IN (
						SELECT TOP (@batch) [Id] FROM {_schema}.{_table} WITH (UPDLOCK, READPAST, ROWLOCK)
						WHERE ([State] = 0 OR [State] = 2)
						  AND ([LeaseExpiresUtc] IS NULL OR [LeaseExpiresUtc] < @now)
						  AND ([NextAttemptUtc] IS NULL OR [NextAttemptUtc] <= @now)
						ORDER BY [CreatedUtc], [Id]
					)
					""",
					connection
				);
				command.Parameters.AddWithValue("@owner", leaseOwner);
				command.Parameters.AddWithValue("@leaseUntil", leaseUntil);
				command.Parameters.AddWithValue("@batch", batchSize);
				command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);

				List<OutboxEnvelope> messages = [];
				await using var reader = await command.ExecuteReaderAsync(token);
				while (await reader.ReadAsync(token))
					messages.Add(ReadMessage(reader));

				return messages;
			},
			cancellationToken
		);

	/// <inheritdoc/>
	public Task CompleteAsync(string id, CancellationToken cancellationToken) =>
		ExecuteWithConnectionAsync(
			async (connection, token) =>
			{
				await using SqlCommand command = new(
					$"""
					UPDATE {_schema}.{_table}
					SET [State] = 1, [DispatchedUtc] = @now, [LeaseExpiresUtc] = NULL,
						[LeaseOwner] = NULL, [LastError] = NULL
					WHERE [Id] = @id
					""",
					connection
				);
				command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);
				command.Parameters.AddWithValue("@id", id);
				await command.ExecuteNonQueryAsync(token);
			},
			cancellationToken
		);

	/// <inheritdoc/>
	public Task MarkFailedAsync(
		string id,
		string errorMessage,
		DateTimeOffset nextAttemptUtc,
		CancellationToken cancellationToken
	) =>
		ExecuteWithConnectionAsync(
			async (connection, token) =>
			{
				await using SqlCommand command = new(
					$"""
					UPDATE {_schema}.{_table}
					SET [State] = 2, [AttemptCount] = [AttemptCount] + 1, [NextAttemptUtc] = @next,
						[LeaseExpiresUtc] = NULL, [LeaseOwner] = NULL, [LastError] = @errorMessage
					WHERE [Id] = @id
					""",
					connection
				);
				command.Parameters.AddWithValue("@next", nextAttemptUtc);
				command.Parameters.AddWithValue("@errorMessage", errorMessage);
				command.Parameters.AddWithValue("@id", id);
				await command.ExecuteNonQueryAsync(token);
			},
			cancellationToken
		);

	/// <inheritdoc/>
	public Task MarkPoisonedAsync(string id, string errorMessage, CancellationToken cancellationToken) =>
		ExecuteWithConnectionAsync(
			async (connection, token) =>
			{
				await using SqlCommand command = new(
					$"""
					UPDATE {_schema}.{_table}
					SET [State] = 3, [LeaseExpiresUtc] = NULL, [LeaseOwner] = NULL, [LastError] = @errorMessage
					WHERE [Id] = @id
					""",
					connection
				);
				command.Parameters.AddWithValue("@errorMessage", errorMessage);
				command.Parameters.AddWithValue("@id", id);
				await command.ExecuteNonQueryAsync(token);
			},
			cancellationToken
		);

	/// <inheritdoc/>
	public Task<int> CleanupAsync(TimeSpan retention, CancellationToken cancellationToken) =>
		ExecuteWithConnectionAsync(
			async (connection, token) =>
			{
				await using SqlCommand command = new(
					$"""
					DELETE FROM {_schema}.{_table}
					WHERE [CreatedUtc] < @cutoff AND ([State] = 1 OR [State] = 3)
					""",
					connection
				);
				command.Parameters.AddWithValue("@cutoff", DateTimeOffset.UtcNow - retention);
				return await command.ExecuteNonQueryAsync(token);
			},
			cancellationToken
		);

	/// <inheritdoc/>
	public async Task<IReadOnlyList<OutboxEnvelope>> GetPoisonedAsync(
		int skip,
		int take,
		CancellationToken cancellationToken
	) =>
		await ExecuteWithConnectionAsync(
			async (connection, token) =>
			{
				await using SqlCommand command = new(
					$"""
					SELECT [Id], [AggregateType], [AggregateId], [EventType], [PayloadJson],
						[IdempotencyKey], [CorrelationId], [CreatedUtc], [State], [AttemptCount],
						[NextAttemptUtc], [DispatchedUtc], [LeaseExpiresUtc], [LastError]
					FROM {_schema}.{_table}
					WHERE [State] = 3
					ORDER BY [CreatedUtc] DESC, [Id]
					OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
					""",
					connection
				);
				command.Parameters.AddWithValue("@skip", skip);
				command.Parameters.AddWithValue("@take", take);

				List<OutboxEnvelope> messages = [];
				await using var reader = await command.ExecuteReaderAsync(token);
				while (await reader.ReadAsync(token))
					messages.Add(ReadMessage(reader));

				return messages;
			},
			cancellationToken
		);

	async Task<int> EnqueueCoreAsync(
		DbConnection connection,
		DbTransaction? transaction,
		OutboxEnvelope message,
		CancellationToken cancellationToken
	)
	{
		await using SqlCommand command = new(
			$"""
			INSERT INTO {_schema}.{_table}
				([Id], [AggregateType], [AggregateId], [EventType], [PayloadJson], [IdempotencyKey],
				 [CorrelationId], [CreatedUtc], [State], [AttemptCount], [NextAttemptUtc],
				 [DispatchedUtc], [LeaseExpiresUtc], [LeaseOwner], [LastError])
			SELECT @id, @aggregateType, @aggregateId, @eventType, @payload, @idempotencyKey,
				   @correlationId, @createdUtc, 0, 0, NULL, NULL, NULL, NULL, NULL
			WHERE NOT EXISTS (
				SELECT 1 FROM {_schema}.{_table} WHERE [IdempotencyKey] = @idempotencyKey
			)
			""",
			(SqlConnection)connection
		);
		if (transaction is not null)
			command.Transaction = (SqlTransaction)transaction;

		command.Parameters.AddWithValue("@id", message.Id);
		command.Parameters.AddWithValue("@aggregateType", message.AggregateType);
		command.Parameters.AddWithValue("@aggregateId", message.AggregateId);
		command.Parameters.AddWithValue("@eventType", message.EventType);
		command.Parameters.AddWithValue("@payload", message.PayloadJson);
		command.Parameters.AddWithValue("@idempotencyKey", (object?)message.IdempotencyKey ?? DBNull.Value);
		command.Parameters.AddWithValue("@correlationId", (object?)message.CorrelationId ?? DBNull.Value);
		command.Parameters.AddWithValue("@createdUtc", message.CreatedUtc);

		return await command.ExecuteNonQueryAsync(cancellationToken);
	}

	async Task<T> ExecuteWithConnectionAsync<T>(
		Func<SqlConnection, CancellationToken, Task<T>> operation,
		CancellationToken cancellationToken
	)
	{
		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);
		return await operation(connection, cancellationToken);
	}

	async Task ExecuteWithConnectionAsync(
		Func<SqlConnection, CancellationToken, Task> operation,
		CancellationToken cancellationToken
	)
	{
		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);
		await operation(connection, cancellationToken);
	}

	async Task EnsureSchemaAsync(CancellationToken cancellationToken)
	{
		if (_schemaEnsured)
			return;

		if (outboxOptions.Value.AutoCreateTable)
		{
			await using SqlConnection connection = new(_connectionString);
			await connection.OpenAsync(cancellationToken);

			var indexName = QuoteIdentifier($"UX_{outboxOptions.Value.TableName}_IdempotencyKey");
			await using SqlCommand command = new(
				$"""
				IF OBJECT_ID(N'{_schema}.{_table}', N'U') IS NULL
				BEGIN
					CREATE TABLE {_schema}.{_table} (
						[Id] NVARCHAR(64) NOT NULL PRIMARY KEY,
						[AggregateType] NVARCHAR(256) NOT NULL,
						[AggregateId] NVARCHAR(256) NOT NULL,
						[EventType] NVARCHAR(256) NOT NULL,
						[PayloadJson] NVARCHAR(MAX) NOT NULL,
						[IdempotencyKey] NVARCHAR(256) NULL,
						[CorrelationId] NVARCHAR(128) NULL,
						[CreatedUtc] DATETIMEOFFSET NOT NULL,
						[State] INT NOT NULL,
						[AttemptCount] INT NOT NULL,
						[NextAttemptUtc] DATETIMEOFFSET NULL,
						[DispatchedUtc] DATETIMEOFFSET NULL,
						[LeaseExpiresUtc] DATETIMEOFFSET NULL,
						[LeaseOwner] NVARCHAR(128) NULL,
						[LastError] NVARCHAR(MAX) NULL
					);
					CREATE UNIQUE INDEX {indexName} ON {_schema}.{_table} ([IdempotencyKey])
						WHERE [IdempotencyKey] IS NOT NULL;
					CREATE INDEX IX_{outboxOptions.Value.TableName}_Dispatch ON {_schema}.{_table}
						([State], [LeaseExpiresUtc], [NextAttemptUtc], [CreatedUtc]);
				END
				""",
				connection
			);
			await command.ExecuteNonQueryAsync(cancellationToken);
		}

		_schemaEnsured = true;
	}

	[LoggerMessage(LogLevel.Debug, Message = "Enqueued outbox message {OutboxMessageId}.")]
	private static partial void LogEnqueued(ILogger logger, string outboxMessageId);

	static OutboxEnvelope ReadMessage(DbDataReader reader) =>
		new(
			reader.GetString(0),
			reader.GetString(1),
			reader.GetString(2),
			reader.GetString(3),
			reader.GetString(4),
			reader.IsDBNull(5) ? null : reader.GetString(5),
			reader.IsDBNull(6) ? null : reader.GetString(6),
			reader.GetFieldValue<DateTimeOffset>(7)
		)
		{
			State = (OutboxState)reader.GetInt32(8),
			AttemptCount = reader.GetInt32(9),
			NextAttemptUtc = reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
			DispatchedUtc = reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
			LeaseExpiresUtc = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
			LastError = reader.IsDBNull(13) ? null : reader.GetString(13),
		};

	static string ResolveConnectionString(
		SqlServerOutboxStoreOptions outboxOptions,
		SqlServerEventStoreOptions eventStoreOptions
	) =>
		outboxOptions.ConnectionString
		?? eventStoreOptions.ConnectionString
		?? throw new InvalidOperationException(
			"SqlServerOutboxStore requires a connection string. Configure 'EventStore:SqlServer:Outbox:ConnectionString' or 'EventStore:SqlServer:ConnectionString'."
		);

	static string QuoteIdentifier(string identifier)
	{
		if (!ValidIdentifier().IsMatch(identifier))
			throw new InvalidOperationException(
				$"The outbox table identifier '{identifier}' is invalid. Identifiers may only contain letters, digits, underscores, dots, and hyphens."
			);

		// SQL Server identifiers are quoted with square brackets, and any closing bracket inside the identifier is escaped by doubling it.
		return $"[{identifier}]";
	}
}
