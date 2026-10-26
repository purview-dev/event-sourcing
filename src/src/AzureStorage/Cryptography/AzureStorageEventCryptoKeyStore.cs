using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.AzureStorage.Cryptography;

/// <summary>
/// A durable <see cref="IEventCryptoKeyStore"/> backed by an Azure Storage table.
/// </summary>
/// <remarks>
/// <para>
/// Keys are scoped to the current tenant: the same data-subject identifier in two tenants is two
/// different people, so they get two different keys, and neither tenant can read or erase the
/// other's. The tenant is part of the partition key rather than a filtered property, so the scoping
/// is structural - a query cannot forget it.
/// </para>
/// <para>
/// <strong>Erasure is only as irreversible as the least-irreversible copy of the key.</strong> A
/// storage account with soft delete or point-in-time restore enabled keeps a deleted key
/// recoverable for the retention window, so a shredded subject is not actually erased until it
/// expires. Disable those on the key table, or accept the delay.
/// </para>
/// </remarks>
public sealed class AzureStorageEventCryptoKeyStore : IEventCryptoKeyStore, IDisposable
{
	readonly TableClient _tableClient;
	readonly ICurrentTenant _currentTenant;
	readonly SemaphoreSlim _tableLock = new(1, 1);
	bool _tableEnsured;

	/// <summary>Creates the store.</summary>
	/// <param name="keyStoreOptions">The key-store options.</param>
	/// <param name="eventStoreOptions">The event-store options, used for any value not overridden.</param>
	/// <param name="currentTenant">The tenant scope keys are read and written under.</param>
	public AzureStorageEventCryptoKeyStore(
		IOptions<AzureStorageEventCryptoKeyStoreOptions> keyStoreOptions,
		IOptions<AzureStorageEventStoreOptions> eventStoreOptions,
		ICurrentTenant? currentTenant = null
	)
	{
		ArgumentNullException.ThrowIfNull(keyStoreOptions);
		ArgumentNullException.ThrowIfNull(eventStoreOptions);

		var connectionString =
			keyStoreOptions.Value.ConnectionString
			?? eventStoreOptions.Value.ConnectionString
			?? throw new InvalidOperationException(
				"AzureStorageEventCryptoKeyStore requires a connection string. Configure 'EventStore:AzureStorage:Cryptography:ConnectionString' or 'EventStore:AzureStorage:ConnectionString'."
			);

		_currentTenant = currentTenant ?? new SystemCurrentTenant();
		_tableClient = new(connectionString, keyStoreOptions.Value.TableName);
	}

	/// <inheritdoc/>
	public async Task<byte[]> GetOrCreateAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		await EnsureTableAsync(cancellationToken);

		var existing = await TryGetAsync(dataSubjectId, cancellationToken);
		if (existing is not null)
			return existing;

		CryptoKeyEntity entity = new()
		{
			PartitionKey = PartitionKeyFor(),
			RowKey = RowKeyFor(dataSubjectId),
			KeyMaterial = AesGcmEventPayloadProtector.CreateKey(),
			CreatedUtc = DateTimeOffset.UtcNow,
		};

		try
		{
			await _tableClient.AddEntityAsync(entity, cancellationToken);

			return entity.KeyMaterial;
		}
		catch (RequestFailedException ex) when (ex.Status == 409)
		{
			// Another caller created the key between the read and the add. Both must end up with the
			// same key, or payloads written by the loser become unreadable - so return theirs, not
			// the one just generated.
			return await TryGetAsync(dataSubjectId, cancellationToken)
				?? throw new InvalidOperationException(
					$"The key for data subject '{dataSubjectId}' could not be read back after a concurrent creation."
				);
		}
	}

	/// <inheritdoc/>
	public async Task<byte[]?> TryGetAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		await EnsureTableAsync(cancellationToken);

		var response = await _tableClient.GetEntityIfExistsAsync<CryptoKeyEntity>(
			PartitionKeyFor(),
			RowKeyFor(dataSubjectId),
			cancellationToken: cancellationToken
		);

		return response.HasValue ? response.Value!.KeyMaterial : null;
	}

	/// <inheritdoc/>
	public async Task<bool> ShredAsync(string dataSubjectId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataSubjectId);

		await EnsureTableAsync(cancellationToken);

		var response = await _tableClient.DeleteEntityAsync(
			PartitionKeyFor(),
			RowKeyFor(dataSubjectId),
			cancellationToken: cancellationToken
		);

		// Delete is idempotent in Table Storage and reports 404 when nothing was there.
		return !response.IsError && response.Status != 404;
	}

	async Task EnsureTableAsync(CancellationToken cancellationToken)
	{
		if (_tableEnsured)
			return;

		await _tableLock.WaitAsync(cancellationToken);
		try
		{
			if (_tableEnsured)
				return;

			await _tableClient.CreateIfNotExistsAsync(cancellationToken);
			_tableEnsured = true;
		}
		finally
		{
			_tableLock.Release();
		}
	}

	string PartitionKeyFor() => AzureTableKeyEncoding.Encode(TenantColumn.Encode(_currentTenant.Current));

	static string RowKeyFor(string dataSubjectId) => AzureTableKeyEncoding.Encode(dataSubjectId);

	/// <inheritdoc/>
	public void Dispose() => _tableLock.Dispose();

	sealed class CryptoKeyEntity : ITableEntity
	{
		public byte[] KeyMaterial { get; set; } = default!;

		public DateTimeOffset CreatedUtc { get; set; }

		public string PartitionKey { get; set; } = default!;

		public string RowKey { get; set; } = default!;

		public DateTimeOffset? Timestamp { get; set; }

		public ETag ETag { get; set; }
	}
}
