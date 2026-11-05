using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Admin.MongoDB;

/// <summary>
/// Reads the MongoDB audit collection written by <see cref="MongoDBAdminAuditLogger"/>.
/// </summary>
/// <remarks>
/// Every read is filtered on <see cref="ICurrentTenant"/>. A tenant scope sees only its own entries;
/// <see cref="TenantScope.System"/> sees all of them. An unowned entry is deliberately <em>not</em>
/// visible to a tenant scope: a missing stamp should look like absent data, not like another
/// tenant's audit trail.
/// </remarks>
/// <param name="client">The Mongo client.</param>
/// <param name="options">The audit options, which name the collection.</param>
/// <param name="currentTenant">Supplies the scope every read is filtered on.</param>
public sealed class MongoDBAdminAuditEntryReader(
	IMongoClient client,
	IOptions<MongoDBAdminAuditLoggerOptions> options,
	ICurrentTenant currentTenant
) : IAdminAuditEntryReader
{
	/// <inheritdoc/>
	public async Task<AdminAuditPage> QueryAsync(AdminAuditQuery query, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(options);

		var page = Math.Max(1, query.Page);
		var pageSize = Math.Clamp(query.PageSize, 1, AdminAuditQuery.MaximumPageSize);

		var builder = Builders<MongoDBAdminAuditDocument>.Filter;
		var filter = builder.Empty;

		var scope = currentTenant.Current;
		if (!scope.IsSystem)
			// Fail closed: a null TenantId is not "visible to everyone", it is "visible to System only".
			filter &= builder.Eq(document => document.TenantId, scope.TenantId);

		if (query.FromUtc is not null)
			filter &= builder.Gte(document => document.TimestampUtc, query.FromUtc.Value.UtcDateTime);

		if (query.ToUtc is not null)
			filter &= builder.Lt(document => document.TimestampUtc, query.ToUtc.Value.UtcDateTime);

		if (query.Feature is not null)
			filter &= builder.Eq(document => document.Feature, query.Feature.Value.ToString());

		if (query.Principal is not null)
			filter &= builder.Eq(document => document.Principal, query.Principal);

		if (query.Target is not null)
			filter &= builder.Eq(document => document.Target, query.Target);

		if (query.Succeeded is not null)
			filter &= builder.Eq(document => document.Succeeded, query.Succeeded.Value);

		if (query.CorrelationId is not null)
			filter &= builder.Eq(document => document.CorrelationId, query.CorrelationId);

		// One document beyond the page answers "is there more" without counting a collection that
		// only ever grows.
		var documents = await MongoDBAdminAuditLogger
			.Collection(client, options.Value)
			.Find(filter)
			.SortByDescending(document => document.TimestampUtc)
			.Skip((page - 1) * pageSize)
			.Limit(pageSize + 1)
			.ToListAsync(cancellationToken);

		var hasMore = documents.Count > pageSize;
		if (hasMore)
			documents.RemoveAt(documents.Count - 1);

		return new AdminAuditPage([.. documents.Select(document => document.ToEntry())], page, pageSize, hasMore);
	}
}
