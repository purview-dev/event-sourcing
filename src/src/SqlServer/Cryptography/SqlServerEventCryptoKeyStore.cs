using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.SqlServer.Events;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.SqlServer.Cryptography;

// SQL strings are built from validated identifiers at construction time, not from user input.
#pragma warning disable CA2100

/// <summary>
/// A durable <see cref="IEventCryptoKeyStore"/> backed by a SQL Server table.
/// </summary>
/// <remarks>
/// <para>
/// Keys are scoped to the current tenant: the same data-subject identifier in two tenants is two
/// different people, so they get two different keys, and neither tenant can read or erase the
/// other's.
/// </para>
/// <para>
/// <strong>Erasure is only as irreversible as the least-irreversible copy of the key.</strong> A
/// point-in-time restore of this table brings shredded keys back, so a deployment that must erase
/// on request has to exclude it from long-retention backups, or accept that erasure completes only
/// when the last backup containing the key ages out.
/// </para>
/// </remarks>
public sealed partial class SqlServerEventCryptoKeyStore : ISuppliedKeyEventCryptoKeyStore, IDisposable
{
	[GeneratedRegex("^[\\w\\-.]+$", RegexOptions.Compiled)]
	private static partial Regex ValidIdentifier();

	readonly IOptions<SqlServerEventCryptoKeyStoreOptions> _options;
	readonly ICurrentTenant _currentTenant;
	readonly string _connectionString;
	readonly string _schema;
	readonly string _table;
	readonly SemaphoreSlim _schemaLock = new(1, 1);
	bool _schemaEnsured;

	/// <summary>Creates the store.</summary>
	/// <param name="keyStoreOptions">The key-store options.</param>
	/// <param name="eventStoreOptions">The event-store options, used for any value not overridden.</param>
	/// <param name="currentTenant">The tenant scope keys are read and written under.</param>
	public SqlServerEventCryptoKeyStore(
		IOptions<SqlServerEventCryptoKeyStoreOptions> keyStoreOptions,
		IOptions<SqlServerEventStoreOptions> eventStoreOptions,
		ICurrentTenant? currentTenant = null
	)
	{
		ArgumentNullException.ThrowIfNull(keyStoreOptions);
		ArgumentNullException.ThrowIfNull(eventStoreOptions);

		_options = keyStoreOptions;
		_currentTenant = currentTenant ?? new SystemCurrentTenant();
		_connectionString = ResolveConnectionString(keyStoreOptions.Value, eventStoreOptions.Value);
		_schema = QuoteIdentifier(keyStoreOptions.Value.SchemaName);
		_table = QuoteIdentifier(keyStoreOptions.Value.TableName);
	}

	/// <inheritdoc/>
	public Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken) =>
		GetOrAddAsync(dataSubjectId, AesGcmEventPayloadProtector.CreateKey(), cancellationToken);

	/// <inheritdoc/>
	public async Task<byte[]> GetOrAddAsync(
		string dataSubjectId,
		byte[] candidateKey,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);
		ArgumentNullException.ThrowIfNull(candidateKey);

		var existing = await TryGetAsync(dataSubjectId, cancellationToken);
		if (existing is not null)
			return existing;

		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		// Insert-if-absent, then re-read: two callers racing to create the first key for a subject
		// must end up with the same key, or payloads written by the loser become unreadable.
		await using SqlCommand command = new(
			$"""
			IF NOT EXISTS (
				SELECT 1 FROM {_schema}.{_table} WHERE [TenantId] = @tenantId AND [DataSubjectId] = @subject
			)
			BEGIN
				BEGIN TRY
					INSERT INTO {_schema}.{_table} ([TenantId], [DataSubjectId], [KeyMaterial], [CreatedUtc])
					VALUES (@tenantId, @subject, @key, @now);
				END TRY
				BEGIN CATCH
					IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
				END CATCH
			END
			""",
			connection
		);
		AddKeyParameters(command, dataSubjectId);
		command.Parameters.AddWithValue("@key", candidateKey);
		command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);

		await command.ExecuteNonQueryAsync(cancellationToken);

		return await TryGetAsync(dataSubjectId, cancellationToken)
			?? throw new InvalidOperationException(
				$"The key for data subject '{dataSubjectId}' could not be read back immediately after being created."
			);
	}

	/// <inheritdoc/>
	public async Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		await EnsureSchemaAsync(cancellationToken);

		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		await using SqlCommand command = new(
			$"""
			SELECT [KeyMaterial] FROM {_schema}.{_table}
			WHERE [TenantId] = @tenantId AND [DataSubjectId] = @subject
			""",
			connection
		);
		AddKeyParameters(command, dataSubjectId);

		var result = await command.ExecuteScalarAsync(cancellationToken);

		return result as byte[];
	}

	/// <inheritdoc/>
	public async Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		await EnsureSchemaAsync(cancellationToken);

		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		await using SqlCommand command = new(
			$"DELETE FROM {_schema}.{_table} WHERE [TenantId] = @tenantId AND [DataSubjectId] = @subject",
			connection
		);
		AddKeyParameters(command, dataSubjectId);

		return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
	}

	void AddKeyParameters(SqlCommand command, string dataSubjectId)
	{
		command.Parameters.AddWithValue("@tenantId", TenantColumn.Encode(_currentTenant.Current));
		command.Parameters.AddWithValue("@subject", dataSubjectId);
	}

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
		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		var indexName = QuoteIdentifier($"UX_{_options.Value.TableName}_Subject");

		// 2714 is "object already exists": another process won the race between the OBJECT_ID check
		// and the CREATE, which is the outcome we wanted anyway.
		await using SqlCommand command = new(
			$"""
			BEGIN TRY
				IF OBJECT_ID(N'{_schema}.{_table}', N'U') IS NULL
				BEGIN
					CREATE TABLE {_schema}.{_table} (
						[TenantId] NVARCHAR(256) NOT NULL,
						[DataSubjectId] NVARCHAR(256) NOT NULL,
						[KeyMaterial] VARBINARY(256) NOT NULL,
						[CreatedUtc] DATETIMEOFFSET NOT NULL
					);
					CREATE UNIQUE INDEX {indexName} ON {_schema}.{_table} ([TenantId], [DataSubjectId]);
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

	/// <inheritdoc/>
	public void Dispose() => _schemaLock.Dispose();

	static string ResolveConnectionString(
		SqlServerEventCryptoKeyStoreOptions keyStoreOptions,
		SqlServerEventStoreOptions eventStoreOptions
	) =>
		keyStoreOptions.ConnectionString
		?? eventStoreOptions.ConnectionString
		?? throw new InvalidOperationException(
			"SqlServerEventCryptoKeyStore requires a connection string. Configure 'EventStore:SqlServer:Cryptography:ConnectionString' or 'EventStore:SqlServer:ConnectionString'."
		);

	static string QuoteIdentifier(string identifier)
	{
		if (!ValidIdentifier().IsMatch(identifier))
			throw new InvalidOperationException(
				$"The crypto key table identifier '{identifier}' is invalid. Identifiers may only contain letters, digits, underscores, dots, and hyphens."
			);

		return $"[{identifier}]";
	}
}
