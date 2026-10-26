using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.SqlServer;
using Purview.EventSourcing.Fixtures.SqlServer;
using Purview.EventSourcing.SqlServer.Events;

namespace Purview.EventSourcing.SqlServer.Auditing;

/// <summary>
/// The durable admin audit logger against real SQL Server.
/// </summary>
/// <remarks>
/// In a regulated deployment the audit trail is the control, so the behaviour worth protecting is
/// that an entry is actually written - and that a deployment which cannot write one finds out,
/// rather than proceeding unaudited by accident.
/// </remarks>
[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class SqlServerAdminAuditLoggerTests(SqlServerEventStoreFixture fixture)
{
	[Test]
	public async Task LogAsync_WritesTheEntry(CancellationToken cancellationToken)
	{
		var table = $"AuditLog_{Guid.NewGuid():N}";
		using var logger = CreateLogger(table);

		await logger.LogAsync(Entry(target: "aggregate-1"), cancellationToken);

		var rows = await ReadAllAsync(table, cancellationToken);

		await Assert.That(rows.Count).IsEqualTo(1);
		await Assert.That(rows[0].Target).IsEqualTo("aggregate-1");
		await Assert.That(rows[0].Feature).IsEqualTo(nameof(AdminFeature.ViewCapabilities));
	}

	[Test]
	public async Task LogAsync_RecordsTheTenantAndCorrelation(CancellationToken cancellationToken)
	{
		var table = $"AuditLog_{Guid.NewGuid():N}";
		using var logger = CreateLogger(table);

		await logger.LogAsync(Entry() with { TenantId = "acme", CorrelationId = "corr-1" }, cancellationToken);

		var rows = await ReadAllAsync(table, cancellationToken);

		await Assert.That(rows[0].TenantId).IsEqualTo("acme");
		await Assert.That(rows[0].CorrelationId).IsEqualTo("corr-1");
	}

	// A refused operation is the one most worth recording, so failure must be audited too.
	[Test]
	public async Task LogAsync_RecordsAFailedOperation(CancellationToken cancellationToken)
	{
		var table = $"AuditLog_{Guid.NewGuid():N}";
		using var logger = CreateLogger(table);

		await logger.LogAsync(Entry() with { Succeeded = false, Details = "denied" }, cancellationToken);

		var rows = await ReadAllAsync(table, cancellationToken);

		await Assert.That(rows[0].Succeeded).IsFalse();
		await Assert.That(rows[0].Details).IsEqualTo("denied");
	}

	// Fail is the default: an action nobody can prove happened is worse than an action that did not.
	[Test]
	public async Task LogAsync_WhenTheEntryCannotBeWritten_FailsByDefault(CancellationToken cancellationToken)
	{
		using var logger = CreateLogger(
			$"AuditLog_{Guid.NewGuid():N}",
			connectionString: "Server=127.0.0.1,1;Database=absent;User Id=sa;Password=no;Encrypt=False;Connect Timeout=1"
		);

		await Assert.That(async () => await logger.LogAsync(Entry(), cancellationToken)).ThrowsException();
	}

	// Continue is a deliberate choice: the operation proceeds unaudited and the gap is logged.
	[Test]
	public async Task LogAsync_WhenConfiguredToContinue_DoesNotThrow(CancellationToken cancellationToken)
	{
		using var logger = CreateLogger(
			$"AuditLog_{Guid.NewGuid():N}",
			connectionString: "Server=127.0.0.1,1;Database=absent;User Id=sa;Password=no;Encrypt=False;Connect Timeout=1",
			failureMode: AdminAuditFailureMode.Continue
		);

		await Assert.That(async () => await logger.LogAsync(Entry(), cancellationToken)).ThrowsNothing();
	}

	static AdminAuditEntry Entry(string? target = null) =>
		new(
			DateTimeOffset.UtcNow,
			AdminFeature.ViewCapabilities,
			"Read",
			"an-administrator",
			target,
			Succeeded: true,
			Details: null
		);

	SqlServerAdminAuditLogger CreateLogger(
		string tableName,
		string? connectionString = null,
		AdminAuditFailureMode failureMode = AdminAuditFailureMode.Fail
	) =>
		new(
			Options.Create(
				new SqlServerAdminAuditLoggerOptions
				{
					ConnectionString = connectionString ?? fixture.ConnectionString,
					TableName = tableName,
					FailureMode = failureMode,
				}
			),
			Options.Create(new SqlServerEventStoreOptions { ConnectionString = fixture.ConnectionString }),
			NullLogger<SqlServerAdminAuditLogger>.Instance
		);

	async Task<IReadOnlyList<AuditRow>> ReadAllAsync(string tableName, CancellationToken cancellationToken)
	{
		await using Microsoft.Data.SqlClient.SqlConnection connection = new(fixture.ConnectionString);
		await connection.OpenAsync(cancellationToken);

#pragma warning disable CA2100
		await using Microsoft.Data.SqlClient.SqlCommand command = new(
			$"SELECT [Feature], [TenantId], [Target], [Succeeded], [Details], [CorrelationId] FROM [dbo].[{tableName}] ORDER BY [Id]",
			connection
		);
#pragma warning restore CA2100

		List<AuditRow> rows = [];
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			rows.Add(
				new(
					reader.GetString(0),
					await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1),
					await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2),
					reader.GetBoolean(3),
					await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4),
					await reader.IsDBNullAsync(5, cancellationToken) ? null : reader.GetString(5)
				)
			);
		}

		return rows;
	}

	sealed record AuditRow(
		string Feature,
		string? TenantId,
		string? Target,
		bool Succeeded,
		string? Details,
		string? CorrelationId
	);
}
