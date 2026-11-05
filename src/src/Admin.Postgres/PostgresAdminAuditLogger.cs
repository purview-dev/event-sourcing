using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Postgres.Events;

namespace Purview.EventSourcing.Admin.Postgres;

// SQL strings are built from validated identifiers at construction time, not from user input.
#pragma warning disable CA2100

/// <summary>
/// A durable <see cref="IAdminAuditLogger"/> backed by an append-only PostgreSQL table.
/// </summary>
/// <remarks>
/// <para>
/// In a regulated deployment the audit trail is the control, so losing it with the process - which
/// is what the in-memory logger does - is not an option. This writes every privileged Admin
/// operation to a table that survives a restart.
/// </para>
/// <para>
/// <strong>Append-only is enforced by what this type does not offer</strong>: there is no update and
/// no delete. The framework cannot grant or revoke database permissions, so a deployment that needs
/// tamper-evidence should also give the application principal <c>INSERT</c> and <c>SELECT</c> on
/// this table and nothing more. A <c>PreviousHash</c> column is reserved so hash chaining can be
/// added later without a migration.
/// </para>
/// </remarks>
public sealed partial class PostgresAdminAuditLogger : IAdminAuditLogger, IDisposable
{
	[GeneratedRegex("^[\\w\\-.]+$", RegexOptions.Compiled)]
	private static partial Regex ValidIdentifier();

	readonly IOptions<PostgresAdminAuditLoggerOptions> _options;
	readonly ILogger<PostgresAdminAuditLogger> _logger;
	readonly string _connectionString;
	readonly string _schema;
	readonly string _table;
	readonly SemaphoreSlim _schemaLock = new(1, 1);
	bool _schemaEnsured;

	/// <summary>Creates the logger.</summary>
	/// <param name="auditOptions">The audit options.</param>
	/// <param name="eventStoreOptions">The event-store options, used for any value not overridden.</param>
	/// <param name="logger">Used to report an audit write failure under the Continue failure mode.</param>
	public PostgresAdminAuditLogger(
		IOptions<PostgresAdminAuditLoggerOptions> auditOptions,
		IOptions<PostgresEventStoreOptions> eventStoreOptions,
		ILogger<PostgresAdminAuditLogger> logger
	)
	{
		ArgumentNullException.ThrowIfNull(auditOptions);
		ArgumentNullException.ThrowIfNull(eventStoreOptions);

		_options = auditOptions;
		_logger = logger;
		_connectionString = ResolveConnectionString(auditOptions.Value, eventStoreOptions.Value);
		_schema = QuoteIdentifier(auditOptions.Value.SchemaName);
		_table = QuoteIdentifier(auditOptions.Value.TableName);
	}

	/// <inheritdoc/>
	public async Task LogAsync(AdminAuditEntry entry, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entry);

		try
		{
			await EnsureSchemaAsync(cancellationToken);

			await using NpgsqlConnection connection = new(_connectionString);
			await connection.OpenAsync(cancellationToken);

			await using NpgsqlCommand command = new(
				$"""
				INSERT INTO {_schema}.{_table}
					("TimestampUtc", "Feature", "Action", "Principal", "TenantId", "Target",
					 "Succeeded", "Details", "CorrelationId")
				VALUES (@timestamp, @feature, @action, @principal, @tenantId, @target,
					 @succeeded, @details, @correlationId);
				""",
				connection
			);
			command.Parameters.AddWithValue("@timestamp", entry.TimestampUtc);
			command.Parameters.AddWithValue("@feature", entry.Feature.ToString());
			command.Parameters.AddWithValue("@action", entry.Action);
			command.Parameters.AddWithValue("@principal", (object?)entry.Principal ?? DBNull.Value);
			command.Parameters.AddWithValue("@tenantId", (object?)entry.TenantId ?? DBNull.Value);
			command.Parameters.AddWithValue("@target", (object?)entry.Target ?? DBNull.Value);
			command.Parameters.AddWithValue("@succeeded", entry.Succeeded);
			command.Parameters.AddWithValue("@details", (object?)entry.Details ?? DBNull.Value);
			command.Parameters.AddWithValue("@correlationId", (object?)entry.CorrelationId ?? DBNull.Value);

			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		catch (Exception ex)
			when (_options.Value.FailureMode == AdminAuditFailureMode.Continue && ex is not OperationCanceledException)
		{
			// Continue was chosen deliberately: the operation proceeds unaudited, and the gap is
			// reported here rather than swallowed entirely.
			LogAuditWriteFailed(_logger, entry.Feature.ToString(), entry.Action, ex);
		}
	}

	[LoggerMessage(
		EventId = 1,
		Level = LogLevel.Error,
		Message = "Failed to write an admin audit entry for {Feature}/{Action}. The operation proceeded unaudited because the configured failure mode is Continue."
	)]
	static partial void LogAuditWriteFailed(ILogger logger, string feature, string action, Exception exception);

	async Task EnsureSchemaAsync(CancellationToken cancellationToken)
	{
		if (_schemaEnsured)
			return;

		if (_options.Value.AutoCreateTable)
		{
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
		await using NpgsqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		// CREATE TABLE IF NOT EXISTS is not safe to run concurrently: PostgreSQL still races on the
		// table's implicit row type and raises 23505.
		//
		// "PreviousHash" is reserved, not used: hash chaining can be added later without a migration.
		await using NpgsqlCommand command = new(
			$"""
			CREATE TABLE IF NOT EXISTS {_schema}.{_table} (
				"Id" BIGSERIAL PRIMARY KEY,
				"TimestampUtc" TIMESTAMPTZ NOT NULL,
				"Feature" VARCHAR(128) NOT NULL,
				"Action" VARCHAR(128) NOT NULL,
				"Principal" VARCHAR(256) NULL,
				"TenantId" VARCHAR(256) NULL,
				"Target" VARCHAR(512) NULL,
				"Succeeded" BOOLEAN NOT NULL,
				"Details" TEXT NULL,
				"CorrelationId" VARCHAR(128) NULL,
				"PreviousHash" BYTEA NULL
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

	/// <inheritdoc/>
	public void Dispose() => _schemaLock.Dispose();

	static string ResolveConnectionString(
		PostgresAdminAuditLoggerOptions auditOptions,
		PostgresEventStoreOptions eventStoreOptions
	) =>
		auditOptions.ConnectionString
		?? eventStoreOptions.ConnectionString
		?? throw new InvalidOperationException(
			"PostgresAdminAuditLogger requires a connection string. Configure 'Admin:Postgres:Audit:ConnectionString' or 'EventStore:Postgres:ConnectionString'."
		);

	static string QuoteIdentifier(string identifier)
	{
		if (!ValidIdentifier().IsMatch(identifier))
			throw new InvalidOperationException(
				$"The admin audit table identifier '{identifier}' is invalid. Identifiers may only contain letters, digits, underscores, dots, and hyphens."
			);

		return $"\"{identifier}\"";
	}
}
