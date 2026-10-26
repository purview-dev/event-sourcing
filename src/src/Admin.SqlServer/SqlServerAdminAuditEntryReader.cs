using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.SqlServer.Events;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Admin.SqlServer;

// SQL strings are built from validated identifiers and parameters, never from caller input.
#pragma warning disable CA2100

/// <summary>
/// Reads the append-only SQL Server audit table written by <see cref="SqlServerAdminAuditLogger"/>.
/// </summary>
/// <remarks>
/// Every read is filtered on <see cref="ICurrentTenant"/>. A tenant scope sees only its own entries;
/// <see cref="TenantScope.System"/> sees all of them, including entries written before a deployment
/// became multi-tenant. Unowned entries are deliberately <em>not</em> visible to a tenant scope: a
/// missing stamp should look like absent data, not like another tenant's audit trail.
/// </remarks>
public sealed partial class SqlServerAdminAuditEntryReader : IAdminAuditEntryReader
{
	[GeneratedRegex("^[\\w\\-.]+$", RegexOptions.Compiled)]
	private static partial Regex ValidIdentifier();

	readonly ICurrentTenant _currentTenant;
	readonly string _connectionString;
	readonly string _schema;
	readonly string _table;

	/// <summary>Creates the reader.</summary>
	/// <param name="auditOptions">The audit options, which name the table.</param>
	/// <param name="eventStoreOptions">The event-store options, used for any value not overridden.</param>
	/// <param name="currentTenant">Supplies the scope every read is filtered on.</param>
	public SqlServerAdminAuditEntryReader(
		IOptions<SqlServerAdminAuditLoggerOptions> auditOptions,
		IOptions<SqlServerEventStoreOptions> eventStoreOptions,
		ICurrentTenant currentTenant
	)
	{
		ArgumentNullException.ThrowIfNull(auditOptions);
		ArgumentNullException.ThrowIfNull(eventStoreOptions);
		ArgumentNullException.ThrowIfNull(currentTenant);

		_currentTenant = currentTenant;
		_connectionString = ResolveConnectionString(auditOptions.Value, eventStoreOptions.Value);
		_schema = QuoteIdentifier(auditOptions.Value.SchemaName);
		_table = QuoteIdentifier(auditOptions.Value.TableName);
	}

	/// <inheritdoc/>
	public async Task<AdminAuditPage> QueryAsync(AdminAuditQuery query, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);

		var page = Math.Max(1, query.Page);
		var pageSize = Math.Clamp(query.PageSize, 1, AdminAuditQuery.MaximumPageSize);

		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);

		StringBuilder predicates = new("WHERE 1 = 1");
		await using SqlCommand command = new();
		command.Connection = connection;

		var scope = _currentTenant.Current;
		if (!scope.IsSystem)
		{
			// Fail closed: NULL is not "visible to everyone", it is "visible to System only".
			predicates.Append(" AND [TenantId] = @tenantId");
			command.Parameters.AddWithValue("@tenantId", scope.TenantId ?? string.Empty);
		}

		Append(
			predicates,
			command,
			query.FromUtc is not null,
			" AND [TimestampUtc] >= @fromUtc",
			"@fromUtc",
			query.FromUtc
		);
		Append(predicates, command, query.ToUtc is not null, " AND [TimestampUtc] < @toUtc", "@toUtc", query.ToUtc);
		Append(
			predicates,
			command,
			query.Feature is not null,
			" AND [Feature] = @feature",
			"@feature",
			query.Feature?.ToString()
		);
		Append(
			predicates,
			command,
			query.Principal is not null,
			" AND [Principal] = @principal",
			"@principal",
			query.Principal
		);
		Append(predicates, command, query.Target is not null, " AND [Target] = @target", "@target", query.Target);
		Append(
			predicates,
			command,
			query.Succeeded is not null,
			" AND [Succeeded] = @succeeded",
			"@succeeded",
			query.Succeeded
		);
		Append(
			predicates,
			command,
			query.CorrelationId is not null,
			" AND [CorrelationId] = @correlationId",
			"@correlationId",
			query.CorrelationId
		);

		// One row beyond the page tells us whether more exist without counting the table, which on an
		// append-only table that only grows would cost a scan on every page.
		command.Parameters.AddWithValue("@skip", (page - 1) * pageSize);
		command.Parameters.AddWithValue("@take", pageSize + 1);
		command.CommandText = $"""
			SELECT [TimestampUtc], [Feature], [Action], [Principal], [TenantId], [Target],
				   [Succeeded], [Details], [CorrelationId]
			FROM {_schema}.{_table}
			{predicates}
			ORDER BY [TimestampUtc] DESC, [Id] DESC
			OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
			""";

		List<AdminAuditEntry> entries = [];
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
			entries.Add(ReadEntry(reader));

		var hasMore = entries.Count > pageSize;
		if (hasMore)
			entries.RemoveAt(entries.Count - 1);

		return new AdminAuditPage(entries, page, pageSize, hasMore);
	}

	static void Append(
		StringBuilder predicates,
		SqlCommand command,
		bool when,
		string clause,
		string name,
		object? value
	)
	{
		if (!when)
			return;

		predicates.Append(clause);
		command.Parameters.AddWithValue(name, value ?? DBNull.Value);
	}

	static AdminAuditEntry ReadEntry(SqlDataReader reader) =>
		new(
			reader.GetDateTimeOffset(0),
			Enum.TryParse<AdminFeature>(reader.GetString(1), out var feature) ? feature : default,
			reader.GetString(2),
			reader.IsDBNull(3) ? null : reader.GetString(3),
			reader.IsDBNull(5) ? null : reader.GetString(5),
			reader.GetBoolean(6),
			reader.IsDBNull(7) ? null : reader.GetString(7)
		)
		{
			TenantId = reader.IsDBNull(4) ? null : reader.GetString(4),
			CorrelationId = reader.IsDBNull(8) ? null : reader.GetString(8),
		};

	static string ResolveConnectionString(
		SqlServerAdminAuditLoggerOptions auditOptions,
		SqlServerEventStoreOptions eventStoreOptions
	) =>
		auditOptions.ConnectionString
		?? eventStoreOptions.ConnectionString
		?? throw new InvalidOperationException(
			"SqlServerAdminAuditEntryReader requires a connection string. Configure 'Admin:SqlServer:Audit:ConnectionString' or 'EventStore:SqlServer:ConnectionString'."
		);

	static string QuoteIdentifier(string identifier)
	{
		if (!ValidIdentifier().IsMatch(identifier))
			throw new InvalidOperationException(
				string.Format(
					CultureInfo.InvariantCulture,
					"The admin audit table identifier '{0}' is invalid. Identifiers may only contain letters, digits, underscores, dots, and hyphens.",
					identifier
				)
			);

		return $"[{identifier}]";
	}
}
