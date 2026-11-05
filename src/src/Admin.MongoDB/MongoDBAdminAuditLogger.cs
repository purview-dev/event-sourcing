using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;

namespace Purview.EventSourcing.Admin.MongoDB;

/// <summary>
/// A durable <see cref="IAdminAuditLogger"/> backed by a MongoDB collection.
/// </summary>
/// <remarks>
/// <para>
/// Closes the gap that a MongoDB deployment previously had: with only the in-memory logger
/// available, the record of who read which payload died with the process, which in a regulated
/// deployment means there was no record.
/// </para>
/// <para>
/// <strong>Append-only is enforced by what this type does not offer</strong>: there is no update and
/// no delete. The framework cannot grant or revoke database roles, so a deployment that needs
/// tamper-evidence should also give the application user <c>insert</c> and <c>find</c> on this
/// collection and nothing more. A <c>PreviousHash</c> field is reserved so hash chaining can be
/// added later without rewriting stored documents.
/// </para>
/// </remarks>
public sealed partial class MongoDBAdminAuditLogger : IAdminAuditLogger, IDisposable
{
	readonly IMongoClient _client;
	readonly IOptions<MongoDBAdminAuditLoggerOptions> _options;
	readonly ILogger<MongoDBAdminAuditLogger> _logger;
	readonly SemaphoreSlim _indexLock = new(1, 1);
	bool _indexEnsured;

	/// <summary>Creates the logger.</summary>
	/// <param name="client">The Mongo client.</param>
	/// <param name="options">The audit options.</param>
	/// <param name="logger">Used to report an audit write failure under the Continue failure mode.</param>
	public MongoDBAdminAuditLogger(
		IMongoClient client,
		IOptions<MongoDBAdminAuditLoggerOptions> options,
		ILogger<MongoDBAdminAuditLogger> logger
	)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(options);

		_client = client;
		_options = options;
		_logger = logger;
	}

	/// <inheritdoc/>
	public async Task LogAsync(AdminAuditEntry entry, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entry);

		try
		{
			await EnsureIndexAsync(cancellationToken);

			await Collection(_client, _options.Value)
				.InsertOneAsync(MongoDBAdminAuditDocument.From(entry), options: null, cancellationToken);
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

	internal static IMongoCollection<MongoDBAdminAuditDocument> Collection(
		IMongoClient client,
		MongoDBAdminAuditLoggerOptions options
	) => client.GetDatabase(options.DatabaseName).GetCollection<MongoDBAdminAuditDocument>(options.CollectionName);

	async Task EnsureIndexAsync(CancellationToken cancellationToken)
	{
		if (_indexEnsured || !_options.Value.AutoCreateIndex)
			return;

		await _indexLock.WaitAsync(cancellationToken);
		try
		{
			if (_indexEnsured)
				return;

			// Tenant first, then time descending: that is exactly the reader's query, and the
			// alternative is a collection scan on a collection that only ever grows.
			var keys = Builders<MongoDBAdminAuditDocument>
				.IndexKeys.Ascending(document => document.TenantId)
				.Descending(document => document.TimestampUtc);

			await Collection(_client, _options.Value)
				.Indexes.CreateOneAsync(
					new CreateIndexModel<MongoDBAdminAuditDocument>(keys),
					options: null,
					cancellationToken
				);

			_indexEnsured = true;
		}
		finally
		{
			_indexLock.Release();
		}
	}

	/// <inheritdoc/>
	public void Dispose() => _indexLock.Dispose();
}
