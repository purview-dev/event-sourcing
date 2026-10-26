using System.Text;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Subscriptions;

namespace Purview.EventSourcing.AzureStorage.Subscriptions;

/// <summary>
/// A durable <see cref="ISubscriptionCheckpointStore"/> backed by an Azure Storage table.
/// </summary>
/// <remarks>
/// Without a durable store every subscription replays every stream after a restart, so this is what
/// makes a subscription usable in a deployed application.
/// </remarks>
public sealed class AzureStorageSubscriptionCheckpointStore : ISubscriptionCheckpointStore, IDisposable
{
	readonly TableClient _tableClient;
	readonly int _maxWriteAttempts;
	readonly SemaphoreSlim _tableLock = new(1, 1);
	bool _tableEnsured;

	/// <summary>Creates the store.</summary>
	/// <param name="checkpointOptions">The checkpoint options.</param>
	/// <param name="eventStoreOptions">The event-store options, used for any value not overridden.</param>
	public AzureStorageSubscriptionCheckpointStore(
		IOptions<AzureStorageSubscriptionCheckpointStoreOptions> checkpointOptions,
		IOptions<AzureStorageEventStoreOptions> eventStoreOptions
	)
	{
		ArgumentNullException.ThrowIfNull(checkpointOptions);
		ArgumentNullException.ThrowIfNull(eventStoreOptions);

		var options = checkpointOptions.Value;
		var connectionString =
			options.ConnectionString
			?? eventStoreOptions.Value.ConnectionString
			?? throw new InvalidOperationException(
				"AzureStorageSubscriptionCheckpointStore requires a connection string. Configure 'EventStore:AzureStorage:Subscriptions:ConnectionString' or 'EventStore:AzureStorage:ConnectionString'."
			);

		_maxWriteAttempts = options.MaxWriteAttempts;
		_tableClient = new(connectionString, options.TableName);
	}

	/// <inheritdoc/>
	public async Task<long?> GetAsync(SubscriptionCheckpointKey key, CancellationToken cancellationToken)
	{
		await EnsureTableAsync(cancellationToken);

		var entity = await ReadAsync(key, cancellationToken);

		return entity?.Version;
	}

	/// <inheritdoc/>
	public async Task SetAsync(SubscriptionCheckpointKey key, long version, CancellationToken cancellationToken)
	{
		await EnsureTableAsync(cancellationToken);

		// Table Storage has no "update only if the stored value is lower", so the guard is a read
		// followed by an ETag-conditional write, retried when another writer gets there first.
		for (var attempt = 0; attempt < _maxWriteAttempts; attempt++)
		{
			var existing = await ReadAsync(key, cancellationToken);

			// A cycle that re-read an earlier range must not rewind a position that is already
			// further on, which would redeliver everything in between.
			if (existing is not null && existing.Version >= version)
				return;

			CheckpointEntity entity = new()
			{
				PartitionKey = PartitionKeyFor(key),
				RowKey = RowKeyFor(key),
				Version = version,
				UpdatedUtc = DateTimeOffset.UtcNow,
			};

			try
			{
				if (existing is null)
					await _tableClient.AddEntityAsync(entity, cancellationToken);
				else
					await _tableClient.UpdateEntityAsync(
						entity,
						existing.ETag,
						TableUpdateMode.Replace,
						cancellationToken
					);

				return;
			}
			catch (RequestFailedException ex) when (ex.Status is 409 or 412)
			{
				// 409: another writer created the row between the read and the add.
				// 412: another writer changed it between the read and the update.
				// Either way, re-read and decide again.
			}
		}

		// Leaving the position behind is safe: a position that is behind redelivers, and delivery is
		// at-least-once already. The next cycle will advance it.
	}

	async Task<CheckpointEntity?> ReadAsync(SubscriptionCheckpointKey key, CancellationToken cancellationToken)
	{
		var response = await _tableClient.GetEntityIfExistsAsync<CheckpointEntity>(
			PartitionKeyFor(key),
			RowKeyFor(key),
			cancellationToken: cancellationToken
		);

		return response.HasValue ? response.Value : null;
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

	// Azure table keys cannot contain '/', '\', '#' or '?', and a delimited key would be ambiguous if
	// any segment contained the delimiter. Base64Url uses only characters that are legal in a key and
	// never produces the '|' separator, so the composed key is both legal and unambiguous.
	static string PartitionKeyFor(SubscriptionCheckpointKey key) =>
		$"{Encode(key.SubscriptionName)}|{Encode(SubscriptionCheckpointTenantColumn.Encode(key.Scope))}";

	static string RowKeyFor(SubscriptionCheckpointKey key) => Encode(key.AggregateId);

	static string Encode(string value) =>
		Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	/// <inheritdoc/>
	public void Dispose() => _tableLock.Dispose();

	sealed class CheckpointEntity : ITableEntity
	{
		public long Version { get; set; }

		public DateTimeOffset UpdatedUtc { get; set; }

		public string PartitionKey { get; set; } = default!;

		public string RowKey { get; set; } = default!;

		public DateTimeOffset? Timestamp { get; set; }

		public ETag ETag { get; set; }
	}
}
