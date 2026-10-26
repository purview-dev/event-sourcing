using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.SqlServer.Events;
using Purview.EventSourcing.Subscriptions;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.SqlServer.Subscriptions;

// SQL strings are built from validated identifiers at construction time, not from user input.
#pragma warning disable CA2100

/// <summary>
/// A durable <see cref="ISubscriptionCheckpointStore"/> backed by a SQL Server table.
/// </summary>
/// <remarks>
/// Without a durable store every subscription replays every stream after a restart, so this is what
/// makes a subscription usable in a deployed application.
/// </remarks>
public sealed partial class SqlServerSubscriptionCheckpointStore(
	IOptions<SqlServerSubscriptionCheckpointStoreOptions> checkpointOptions,
	IOptions<SqlServerEventStoreOptions> eventStoreOptions
) : ISubscriptionCheckpointStore, ISubscriptionCheckpointMaintenance, IDisposable
{
	[GeneratedRegex("^[\\w\\-.]+$", RegexOptions.Compiled)]
	private static partial Regex ValidIdentifier();

	readonly string _connectionString = ResolveConnectionString(checkpointOptions.Value, eventStoreOptions.Value);
	readonly string _schema = QuoteIdentifier(checkpointOptions.Value.SchemaName);
	readonly string _table = QuoteIdentifier(checkpointOptions.Value.TableName);
	readonly SemaphoreSlim _schemaLock = new(1, 1);
	bool _schemaEnsured;

	/// <inheritdoc/>
	public async Task<long?> GetAsync(SubscriptionCheckpointKey key, CancellationToken cancellationToken)
	{
		await EnsureSchemaAsync(cancellationToken);

		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		await using SqlCommand command = new(
			$"""
			SELECT [Version] FROM {_schema}.{_table}
			WHERE [SubscriptionName] = @name AND [TenantId] = @tenantId AND [AggregateId] = @aggregateId
			""",
			connection
		);
		AddKeyParameters(command, key);

		var result = await command.ExecuteScalarAsync(cancellationToken);

		return result is null or DBNull ? null : Convert.ToInt64(result, provider: null);
	}

	/// <inheritdoc/>
	public async Task SetAsync(SubscriptionCheckpointKey key, long version, CancellationToken cancellationToken)
	{
		await EnsureSchemaAsync(cancellationToken);

		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		// The UPDATE is guarded by [Version] < @version so a cycle that re-read an earlier range
		// cannot rewind a position that is already further on, which would redeliver everything in
		// between. The INSERT races another writer creating the same row; losing that race is
		// swallowed, because the worst outcome is a position that is behind, and a position behind
		// redelivers rather than skips.
		await using SqlCommand command = new(
			$"""
			UPDATE {_schema}.{_table}
			SET [Version] = @version, [UpdatedUtc] = @now
			WHERE [SubscriptionName] = @name AND [TenantId] = @tenantId AND [AggregateId] = @aggregateId
			  AND [Version] < @version;

			IF @@ROWCOUNT = 0 AND NOT EXISTS (
				SELECT 1 FROM {_schema}.{_table}
				WHERE [SubscriptionName] = @name AND [TenantId] = @tenantId AND [AggregateId] = @aggregateId
			)
			BEGIN
				BEGIN TRY
					INSERT INTO {_schema}.{_table}
						([SubscriptionName], [TenantId], [AggregateId], [Version], [UpdatedUtc])
					VALUES (@name, @tenantId, @aggregateId, @version, @now);
				END TRY
				BEGIN CATCH
					IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
				END CATCH
			END
			""",
			connection
		);
		AddKeyParameters(command, key);
		command.Parameters.AddWithValue("@version", version);
		command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<int> RemoveSubscriptionAsync(
		string subscriptionName,
		TenantScope scope,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionName);

		await EnsureSchemaAsync(cancellationToken);

		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		// Both columns, always. Deleting on the subscription name alone would remove every tenant's
		// position and replay all of them on the next cycle.
		await using SqlCommand command = new(
			$"""
			DELETE FROM {_schema}.{_table}
			WHERE [SubscriptionName] = @name AND [TenantId] = @tenantId
			""",
			connection
		);
		command.Parameters.AddWithValue("@name", subscriptionName);
		command.Parameters.AddWithValue("@tenantId", TenantColumn.Encode(scope));

		return await command.ExecuteNonQueryAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<bool> RemoveCheckpointAsync(SubscriptionCheckpointKey key, CancellationToken cancellationToken)
	{
		await EnsureSchemaAsync(cancellationToken);

		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		await using SqlCommand command = new(
			$"""
			DELETE FROM {_schema}.{_table}
			WHERE [SubscriptionName] = @name AND [TenantId] = @tenantId AND [AggregateId] = @aggregateId
			""",
			connection
		);
		AddKeyParameters(command, key);

		return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
	}

	static void AddKeyParameters(SqlCommand command, SubscriptionCheckpointKey key)
	{
		command.Parameters.AddWithValue("@name", key.SubscriptionName);
		command.Parameters.AddWithValue("@tenantId", TenantColumn.Encode(key.Scope));
		command.Parameters.AddWithValue("@aggregateId", key.AggregateId);
	}

	async Task EnsureSchemaAsync(CancellationToken cancellationToken)
	{
		if (_schemaEnsured)
			return;

		if (checkpointOptions.Value.AutoCreateTable)
		{
			// IF OBJECT_ID(...) IS NULL is a check, not a lock: two callers can both pass it and
			// both try to create the table. The semaphore serialises this process; the T-SQL CATCH
			// below covers a second process, which no in-process lock can.
			await _schemaLock.WaitAsync(cancellationToken);
			try
			{
				if (_schemaEnsured)
					return;

				await EnsureSchemaCoreAsync(cancellationToken);
			}
			finally
			{
				_schemaLock.Release();
			}
		}

		_schemaEnsured = true;
	}

	async Task EnsureSchemaCoreAsync(CancellationToken cancellationToken)
	{
		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		var indexName = QuoteIdentifier($"UX_{checkpointOptions.Value.TableName}_Key");

		// [TenantId] is NOT NULL and encodes the system scope as an empty string, so the unique
		// index never contains a NULL. A nullable tenant column behaves differently between SQL
		// Server and PostgreSQL - PostgreSQL treats NULLs as distinct - and would silently allow
		// duplicate system-scope rows there.
		//
		// 2714 is "object already exists": another process won the race between the OBJECT_ID check
		// and the CREATE, which is the outcome we wanted anyway.
		await using SqlCommand command = new(
			$"""
			BEGIN TRY
				IF OBJECT_ID(N'{_schema}.{_table}', N'U') IS NULL
				BEGIN
					CREATE TABLE {_schema}.{_table} (
						[SubscriptionName] NVARCHAR(256) NOT NULL,
						[TenantId] NVARCHAR(256) NOT NULL,
						[AggregateId] NVARCHAR(256) NOT NULL,
						[Version] BIGINT NOT NULL,
						[UpdatedUtc] DATETIMEOFFSET NOT NULL
					);
					CREATE UNIQUE INDEX {indexName} ON {_schema}.{_table}
						([SubscriptionName], [TenantId], [AggregateId]);
				END
			END TRY
			BEGIN CATCH
				IF ERROR_NUMBER() <> 2714 THROW;
			END CATCH
			""",
			connection
		);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	static string ResolveConnectionString(
		SqlServerSubscriptionCheckpointStoreOptions checkpointOptions,
		SqlServerEventStoreOptions eventStoreOptions
	) =>
		checkpointOptions.ConnectionString
		?? eventStoreOptions.ConnectionString
		?? throw new InvalidOperationException(
			"SqlServerSubscriptionCheckpointStore requires a connection string. Configure 'EventStore:SqlServer:Subscriptions:ConnectionString' or 'EventStore:SqlServer:ConnectionString'."
		);

	/// <inheritdoc/>
	public void Dispose() => _schemaLock.Dispose();

	static string QuoteIdentifier(string identifier)
	{
		if (!ValidIdentifier().IsMatch(identifier))
			throw new InvalidOperationException(
				$"The subscription checkpoint table identifier '{identifier}' is invalid. Identifiers may only contain letters, digits, underscores, dots, and hyphens."
			);

		return $"[{identifier}]";
	}
}
