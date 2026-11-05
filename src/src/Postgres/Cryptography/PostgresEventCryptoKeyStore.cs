using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Npgsql;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.Postgres.Events;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Postgres.Cryptography;

// SQL strings are built from validated identifiers at construction time, not from user input.
#pragma warning disable CA2100

/// <summary>
/// A durable <see cref="IEventCryptoKeyStore"/> backed by a PostgreSQL table.
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
public sealed partial class PostgresEventCryptoKeyStore
	: ISuppliedKeyEventCryptoKeyStore,
		IRotatingEventCryptoKeyStore,
		IDisposable
{
	[GeneratedRegex("^[\\w\\-.]+$", RegexOptions.Compiled)]
	private static partial Regex ValidIdentifier();

	readonly IOptions<PostgresEventCryptoKeyStoreOptions> _options;
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
	public PostgresEventCryptoKeyStore(
		IOptions<PostgresEventCryptoKeyStoreOptions> keyStoreOptions,
		IOptions<PostgresEventStoreOptions> eventStoreOptions,
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
	public async Task<byte[]> GetOrAddAsync(
		string dataSubjectId,
		byte[] candidateKey,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);
		ArgumentNullException.ThrowIfNull(candidateKey);
		await EnsureSchemaAsync(cancellationToken);

		await using NpgsqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		// Two callers racing to create the first key for a subject must end up with the same one, or
		// payloads written by the loser become unreadable.
		//
		// DO NOTHING is the wrong tool here: it returns no row, and a fallback SELECT in the same
		// statement runs against the same snapshot, so it cannot see a row a concurrent transaction
		// has just inserted - the loser gets nothing at all. A no-op DO UPDATE takes the lock and
		// returns the stored row either way, which is the point.
		await using NpgsqlCommand command = new(
			$"""
			INSERT INTO {_schema}.{_table} ("TenantId", "DataSubjectId", "Generation", "KeyMaterial", "CreatedUtc")
			VALUES (@tenantId, @subject, 1, @key, @now)
			ON CONFLICT ("TenantId", "DataSubjectId", "Generation") DO UPDATE
			SET "KeyMaterial" = {_table}."KeyMaterial"
			RETURNING "KeyMaterial"
			""",
			connection
		);
		AddKeyParameters(command, dataSubjectId);
		command.Parameters.AddWithValue("@key", candidateKey);
		command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);

		var result = await command.ExecuteScalarAsync(cancellationToken);

		return result as byte[]
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
	public async Task<byte[]?> TryGetAsync(string dataSubjectId, string keyId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);
		ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

		if (!int.TryParse(keyId, NumberStyles.None, CultureInfo.InvariantCulture, out var generation))
			return null;

		await EnsureSchemaAsync(cancellationToken);

		await using NpgsqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		await using NpgsqlCommand command = new(
			$"""
			SELECT "KeyMaterial" FROM {_schema}.{_table}
			WHERE "TenantId" = @tenantId AND "DataSubjectId" = @subject AND "Generation" = @generation
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
		// the primary key. Losing is harmless - the winner's generation becomes current - but the
		// loser must observe that rather than report a generation it did not create.
		for (var attempt = 0; attempt < 5; attempt++)
		{
			await using NpgsqlConnection connection = new(_connectionString);
			await connection.OpenAsync(cancellationToken);

			await using NpgsqlCommand command = new(
				$"""
				INSERT INTO {_schema}.{_table} ("TenantId", "DataSubjectId", "Generation", "KeyMaterial", "CreatedUtc")
				SELECT @tenantId, @subject, COALESCE(MAX("Generation"), 0) + 1, @key, @now
				FROM {_schema}.{_table}
				WHERE "TenantId" = @tenantId AND "DataSubjectId" = @subject
				RETURNING "Generation"
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
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				// Another rotation took this generation; recompute and try again.
			}
		}

		throw new InvalidOperationException(
			$"The key for data subject '{dataSubjectId}' could not be rotated after repeated contention."
		);
	}

	async Task<(string KeyId, byte[] Key)?> TryGetCurrentAsync(
		string dataSubjectId,
		CancellationToken cancellationToken
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		await EnsureSchemaAsync(cancellationToken);

		await using NpgsqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		await using NpgsqlCommand command = new(
			$"""
			SELECT "Generation", "KeyMaterial" FROM {_schema}.{_table}
			WHERE "TenantId" = @tenantId AND "DataSubjectId" = @subject
			ORDER BY "Generation" DESC
			LIMIT 1
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

	/// <inheritdoc/>
	public async Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		await EnsureSchemaAsync(cancellationToken);

		await using NpgsqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		await using NpgsqlCommand command = new(
			$"""DELETE FROM {_schema}.{_table} WHERE "TenantId" = @tenantId AND "DataSubjectId" = @subject""",
			connection
		);
		AddKeyParameters(command, dataSubjectId);

		return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
	}

	void AddKeyParameters(NpgsqlCommand command, string dataSubjectId)
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
			// CREATE TABLE IF NOT EXISTS is not safe to run concurrently: PostgreSQL still races on
			// the table's implicit row type and raises 23505.
			await _schemaLock.WaitAsync(cancellationToken);
			try
			{
				if (_schemaEnsured)
					return;

				await using NpgsqlConnection connection = new(_connectionString);
				await connection.OpenAsync(cancellationToken);

				await using NpgsqlCommand command = new(
					$"""
					CREATE TABLE IF NOT EXISTS {_schema}.{_table} (
						"TenantId" VARCHAR(256) NOT NULL,
						"DataSubjectId" VARCHAR(256) NOT NULL,
						"Generation" INTEGER NOT NULL,
						"KeyMaterial" BYTEA NOT NULL,
						"CreatedUtc" TIMESTAMPTZ NOT NULL,
						PRIMARY KEY ("TenantId", "DataSubjectId", "Generation")
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

	/// <inheritdoc/>
	public void Dispose() => _schemaLock.Dispose();

	static string ResolveConnectionString(
		PostgresEventCryptoKeyStoreOptions keyStoreOptions,
		PostgresEventStoreOptions eventStoreOptions
	) =>
		keyStoreOptions.ConnectionString
		?? eventStoreOptions.ConnectionString
		?? throw new InvalidOperationException(
			"PostgresEventCryptoKeyStore requires a connection string. Configure 'EventStore:Postgres:Cryptography:ConnectionString' or 'EventStore:Postgres:ConnectionString'."
		);

	static string QuoteIdentifier(string identifier)
	{
		if (!ValidIdentifier().IsMatch(identifier))
			throw new InvalidOperationException(
				$"The crypto key table identifier '{identifier}' is invalid. Identifiers may only contain letters, digits, underscores, dots, and hyphens."
			);

		return $"\"{identifier}\"";
	}
}
