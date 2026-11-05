using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.AzureStorage;

namespace Purview.EventSourcing.Admin.AzureStorage;

/// <summary>
/// A durable <see cref="IAdminAuditLogger"/> backed by an Azure Storage table.
/// </summary>
/// <remarks>
/// <para>
/// Closes the gap that an Azure Storage deployment previously had: with only the in-memory logger
/// available, the record of who read which payload died with the process, which in a regulated
/// deployment means there was no record.
/// </para>
/// <para>
/// <strong>Append-only is enforced by what this type does not offer</strong>: there is no update and
/// no delete. The framework cannot grant or revoke storage permissions, so a deployment that needs
/// tamper-evidence should issue a SAS limited to add and query on this table. A
/// <c>PreviousHash</c> property is reserved so hash chaining can be added later.
/// </para>
/// <para>
/// The tenant is the partition key rather than a filtered property - see
/// <see cref="AzureStorageAdminAuditKeys"/> for why that matters.
/// </para>
/// </remarks>
public sealed partial class AzureStorageAdminAuditLogger : IAdminAuditLogger, IDisposable
{
	readonly IOptions<AzureStorageAdminAuditLoggerOptions> _options;
	readonly ILogger<AzureStorageAdminAuditLogger> _logger;
	readonly TableClient _table;
	readonly SemaphoreSlim _tableLock = new(1, 1);
	bool _tableEnsured;

	/// <summary>Creates the logger.</summary>
	/// <param name="auditOptions">The audit options.</param>
	/// <param name="eventStoreOptions">The event-store options, used for any value not overridden.</param>
	/// <param name="logger">Used to report an audit write failure under the Continue failure mode.</param>
	public AzureStorageAdminAuditLogger(
		IOptions<AzureStorageAdminAuditLoggerOptions> auditOptions,
		IOptions<AzureStorageEventStoreOptions> eventStoreOptions,
		ILogger<AzureStorageAdminAuditLogger> logger
	)
	{
		ArgumentNullException.ThrowIfNull(auditOptions);
		ArgumentNullException.ThrowIfNull(eventStoreOptions);

		_options = auditOptions;
		_logger = logger;
		_table = CreateTableClient(auditOptions.Value, eventStoreOptions.Value);
	}

	/// <inheritdoc/>
	public async Task LogAsync(AdminAuditEntry entry, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entry);

		try
		{
			await EnsureTableAsync(cancellationToken);

			TableEntity row = new(
				AzureStorageAdminAuditKeys.PartitionFor(entry.TenantId),
				AzureStorageAdminAuditKeys.RowKeyFor(entry.TimestampUtc)
			)
			{
				["TimestampUtc"] = entry.TimestampUtc.UtcDateTime,
				["Feature"] = entry.Feature.ToString(),
				["Action"] = entry.Action,
				["Principal"] = entry.Principal,
				// Stored as well as partitioned on, so a read never has to parse the partition key
				// back into a tenant - and so the system scope can report it without doing so.
				["TenantId"] = entry.TenantId,
				["Target"] = entry.Target,
				["Succeeded"] = entry.Succeeded,
				["Details"] = entry.Details,
				["CorrelationId"] = entry.CorrelationId,
			};

			// Add, never upsert: an upsert would silently overwrite, which is the one thing an
			// append-only trail must not do.
			await _table.AddEntityAsync(row, cancellationToken);
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

	internal static TableClient CreateTableClient(
		AzureStorageAdminAuditLoggerOptions auditOptions,
		AzureStorageEventStoreOptions eventStoreOptions
	)
	{
		var connectionString =
			auditOptions.ConnectionString
			?? eventStoreOptions.ConnectionString
			?? throw new InvalidOperationException(
				"AzureStorageAdminAuditLogger requires a connection string. Configure 'Admin:AzureStorage:Audit:ConnectionString' or the event store's connection string."
			);

		return new TableServiceClient(connectionString).GetTableClient(auditOptions.TableName);
	}

	async Task EnsureTableAsync(CancellationToken cancellationToken)
	{
		if (_tableEnsured || !_options.Value.AutoCreateTable)
			return;

		await _tableLock.WaitAsync(cancellationToken);
		try
		{
			if (_tableEnsured)
				return;

			try
			{
				await _table.CreateIfNotExistsAsync(cancellationToken);
			}
			catch (RequestFailedException ex) when (ex.Status == 409)
			{
				// Another writer created it first, which is the outcome we wanted anyway.
			}

			_tableEnsured = true;
		}
		finally
		{
			_tableLock.Release();
		}
	}

	/// <inheritdoc/>
	public void Dispose() => _tableLock.Dispose();
}
