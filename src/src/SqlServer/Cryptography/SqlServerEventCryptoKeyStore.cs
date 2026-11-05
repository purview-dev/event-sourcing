using System.Globalization;
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
/// A durable, rotating <see cref="IEventCryptoKeyStore"/> backed by a SQL Server table.
/// </summary>
/// <remarks>
/// <para>
/// Keys are scoped to the current tenant: the same data-subject identifier in two tenants is two
/// different people, so they get two different keys, and neither tenant can read or erase the
/// other's.
/// </para>
/// <para>
/// A subject can hold several key generations. The highest is current and is used for new payloads;
/// earlier ones are retained because the events written under them would otherwise be unreadable.
/// Erasure removes every generation - a shred that left one behind would not be an erasure.
/// </para>
/// <para>
/// <strong>Erasure is only as irreversible as the least-irreversible copy of the key.</strong> A
/// point-in-time restore of this table brings shredded keys back, so a deployment that must erase
/// on request has to exclude it from long-retention backups, or accept that erasure completes only
/// when the last backup containing the key ages out.
/// </para>
/// </remarks>
public sealed partial class SqlServerEventCryptoKeyStore
	: ISuppliedKeyEventCryptoKeyStore,
		IRotatingEventCryptoKeyStore,
		IDisposable
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
	public bool SupportsRotation => true;

	/// <inheritdoc/>
	public Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken) =>
		GetOrAddAsync(dataSubjectId, AesGcmEventPayloadProtector.CreateKey(), cancellationToken);

	/// <inheritdoc/>
	public async Task<(string KeyId, byte[] Key)> GetCurrentAsync(
		string dataSubjectId,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		var current = await TryGetCurrentAsync(dataSubjectId, cancellationToken);
		if (current is not null)
			return current.Value;

		await GetOrAddAsync(dataSubjectId, AesGcmEventPayloadProtector.CreateKey(), cancellationToken);

		return await TryGetCurrentAsync(dataSubjectId, cancellationToken)
			?? throw new InvalidOperationException(
				$"The key for data subject '{dataSubjectId}' could not be read back immediately after being created."
			);
	}

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

		// Insert the first generation only if the subject has none. Two callers racing to create it
		// must end up with the same key, or payloads written by the loser become unreadable, so the
		// duplicate-key failure is swallowed and the stored value re-read.
		await using SqlCommand command = new(
			$"""
			IF NOT EXISTS (
				SELECT 1 FROM {_schema}.{_table} WHERE [TenantId] = @tenantId AND [DataSubjectId] = @subject
			)
			BEGIN
				BEGIN TRY
					INSERT INTO {_schema}.{_table}
						([TenantId], [DataSubjectId], [Generation], [KeyMaterial], [CreatedUtc])
					VALUES (@tenantId, @subject, 1, @key, @now);
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
		var current = await TryGetCurrentAsync(dataSubjectId, cancellationToken);

		return current?.Key;
	}

	/// <inheritdoc/>
	public async Task<byte[]?> TryGetAsync(string dataSubjectId, string keyId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);
		ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

		if (!int.TryParse(keyId, NumberStyles.None, CultureInfo.InvariantCulture, out var generation))
			return null;

		await EnsureSchemaAsync(cancellationToken);

		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		await using SqlCommand command = new(
			$"""
			SELECT [KeyMaterial] FROM {_schema}.{_table}
			WHERE [TenantId] = @tenantId AND [DataSubjectId] = @subject AND [Generation] = @generation
			""",
			connection
		);
		AddKeyParameters(command, dataSubjectId);
		command.Parameters.AddWithValue("@generation", generation);

		return await command.ExecuteScalarAsync(cancellationToken) as byte[];
	}

	/// <inheritdoc/>
	public async Task<string> RotateAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		await EnsureSchemaAsync(cancellationToken);

		// Retried because two concurrent rotations compute the same next generation and one loses on
		// the unique index. Losing is harmless - the winner's generation becomes current - but the
		// loser must observe that rather than report a generation it did not create.
		for (var attempt = 0; attempt < 5; attempt++)
		{
			await using SqlConnection connection = new(_connectionString);
			await connection.OpenAsync(cancellationToken);

			await using SqlCommand command = new(
				$"""
				DECLARE @next INT = (
					SELECT ISNULL(MAX([Generation]), 0) + 1 FROM {_schema}.{_table}
					WHERE [TenantId] = @tenantId AND [DataSubjectId] = @subject
				);

				INSERT INTO {_schema}.{_table}
					([TenantId], [DataSubjectId], [Generation], [KeyMaterial], [CreatedUtc])
				VALUES (@tenantId, @subject, @next, @key, @now);

				SELECT @next;
				""",
				connection
			);
			AddKeyParameters(command, dataSubjectId);
			command.Parameters.AddWithValue("@key", AesGcmEventPayloadProtector.CreateKey());
			command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);

			try
			{
				var next = await command.ExecuteScalarAsync(cancellationToken);

				return Convert.ToInt32(next, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
			}
			catch (SqlException ex) when (ex.Number is 2601 or 2627)
			{
				// Another rotation took this generation; recompute and try again.
			}
		}

		throw new InvalidOperationException(
			$"The key for data subject '{dataSubjectId}' could not be rotated after repeated contention."
		);
	}

	/// <inheritdoc/>
	/// <remarks>Removes every generation: an erasure that left one behind would not be an erasure.</remarks>
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

	async Task<(string KeyId, byte[] Key)?> TryGetCurrentAsync(
		string dataSubjectId,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		await EnsureSchemaAsync(cancellationToken);

		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		await using SqlCommand command = new(
			$"""
			SELECT TOP 1 [Generation], [KeyMaterial] FROM {_schema}.{_table}
			WHERE [TenantId] = @tenantId AND [DataSubjectId] = @subject
			ORDER BY [Generation] DESC
			""",
			connection
		);
		AddKeyParameters(command, dataSubjectId);

		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		if (!await reader.ReadAsync(cancellationToken))
			return null;

		var generation = reader.GetInt32(0);

		return (generation.ToString(CultureInfo.InvariantCulture), (byte[])reader[1]);
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
						[Generation] INT NOT NULL,
						[KeyMaterial] VARBINARY(256) NOT NULL,
						[CreatedUtc] DATETIMEOFFSET NOT NULL
					);
					CREATE UNIQUE INDEX {indexName} ON {_schema}.{_table}
						([TenantId], [DataSubjectId], [Generation]);
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
