using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Npgsql;
using Purview.EventSourcing.Postgres.Events;
using Purview.EventSourcing.Subscriptions;

namespace Purview.EventSourcing.Postgres.Subscriptions;

// SQL strings are built from validated identifiers at construction time, not from user input.
#pragma warning disable CA2100

/// <summary>
/// A durable <see cref="ISubscriptionCheckpointStore"/> backed by a PostgreSQL table.
/// </summary>
/// <remarks>
/// Without a durable store every subscription replays every stream after a restart, so this is what
/// makes a subscription usable in a deployed application.
/// </remarks>
public sealed partial class PostgresSubscriptionCheckpointStore(
	IOptions<PostgresSubscriptionCheckpointStoreOptions> checkpointOptions,
	IOptions<PostgresEventStoreOptions> eventStoreOptions
) : ISubscriptionCheckpointStore, IDisposable
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

		await using NpgsqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		await using NpgsqlCommand command = new(
			$"""
			SELECT "Version" FROM {_schema}.{_table}
			WHERE "SubscriptionName" = @name AND "TenantId" = @tenantId AND "AggregateId" = @aggregateId
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

		await using NpgsqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		// The DO UPDATE is guarded so a cycle that re-read an earlier range cannot rewind a position
		// that is already further on, which would redeliver everything in between.
		await using NpgsqlCommand command = new(
			$"""
			INSERT INTO {_schema}.{_table}
				("SubscriptionName", "TenantId", "AggregateId", "Version", "UpdatedUtc")
			VALUES (@name, @tenantId, @aggregateId, @version, @now)
			ON CONFLICT ("SubscriptionName", "TenantId", "AggregateId") DO UPDATE
			SET "Version" = EXCLUDED."Version", "UpdatedUtc" = EXCLUDED."UpdatedUtc"
			WHERE {_table}."Version" < EXCLUDED."Version"
			""",
			connection
		);
		AddKeyParameters(command, key);
		command.Parameters.AddWithValue("@version", version);
		command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	static void AddKeyParameters(NpgsqlCommand command, SubscriptionCheckpointKey key)
	{
		command.Parameters.AddWithValue("@name", key.SubscriptionName);
		command.Parameters.AddWithValue("@tenantId", SubscriptionCheckpointTenantColumn.Encode(key.Scope));
		command.Parameters.AddWithValue("@aggregateId", key.AggregateId);
	}

	async Task EnsureSchemaAsync(CancellationToken cancellationToken)
	{
		if (_schemaEnsured)
			return;

		if (checkpointOptions.Value.AutoCreateTable)
		{
			// CREATE TABLE IF NOT EXISTS is not safe to run concurrently: PostgreSQL still races on
			// the table's implicit row type and raises 23505 on pg_type_typname_nsp_index. The
			// semaphore serialises callers in this process; the catch covers a second process, which
			// no in-process lock can.
			await _schemaLock.WaitAsync(cancellationToken);
			try
			{
				if (_schemaEnsured)
					return;

				await using NpgsqlConnection connection = new(_connectionString);
				await connection.OpenAsync(cancellationToken);

				// "TenantId" is NOT NULL and encodes the system scope as an empty string. PostgreSQL
				// treats NULLs as distinct in a unique index before NULLS NOT DISTINCT (15+), so a
				// nullable tenant column would silently allow duplicate system-scope rows.
				await using NpgsqlCommand command = new(
					$"""
					CREATE TABLE IF NOT EXISTS {_schema}.{_table} (
						"SubscriptionName" VARCHAR(256) NOT NULL,
						"TenantId" VARCHAR(256) NOT NULL,
						"AggregateId" VARCHAR(256) NOT NULL,
						"Version" BIGINT NOT NULL,
						"UpdatedUtc" TIMESTAMPTZ NOT NULL,
						PRIMARY KEY ("SubscriptionName", "TenantId", "AggregateId")
					);
					""",
					connection
				);

				try
				{
					await command.ExecuteNonQueryAsync(cancellationToken);
				}
				catch (PostgresException ex) when (ex.SqlState is "23505" or "42P07")
				{
					// Another writer created it first, which is the outcome we wanted anyway.
				}
			}
			finally
			{
				_schemaLock.Release();
			}
		}

		_schemaEnsured = true;
	}

	static string ResolveConnectionString(
		PostgresSubscriptionCheckpointStoreOptions checkpointOptions,
		PostgresEventStoreOptions eventStoreOptions
	) =>
		checkpointOptions.ConnectionString
		?? eventStoreOptions.ConnectionString
		?? throw new InvalidOperationException(
			"PostgresSubscriptionCheckpointStore requires a connection string. Configure 'EventStore:Postgres:Subscriptions:ConnectionString' or 'EventStore:Postgres:ConnectionString'."
		);

	/// <inheritdoc/>
	public void Dispose() => _schemaLock.Dispose();

	static string QuoteIdentifier(string identifier)
	{
		if (!ValidIdentifier().IsMatch(identifier))
			throw new InvalidOperationException(
				$"The subscription checkpoint table identifier '{identifier}' is invalid. Identifiers may only contain letters, digits, underscores, dots, and hyphens."
			);

		return $"\"{identifier}\"";
	}
}
