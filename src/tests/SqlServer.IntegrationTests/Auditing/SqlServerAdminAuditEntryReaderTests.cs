using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Admin.SqlServer;
using Purview.EventSourcing.Fixtures.SqlServer;
using Purview.EventSourcing.SqlServer.Events;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.SqlServer.Auditing;

/// <summary>
/// Reading the durable admin audit trail back, against real SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests cover the reader's shape against an in-memory sink. These cover the part that
/// only exists in SQL: the <c>WHERE [TenantId] = @tenantId</c> predicate, the <c>OFFSET/FETCH</c>
/// paging, and the newest-first ordering. A LINQ filter passing says nothing about whether the SQL
/// one was written, or written against the right column.
/// </para>
/// <para>
/// <strong>Every test here shares one table.</strong> Both scopes address the same physical storage,
/// because sharing storage is what the isolation is protecting; a per-scope table would pass while
/// proving nothing.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class SqlServerAdminAuditEntryReaderTests(SqlServerEventStoreFixture fixture)
{
	[Test]
	public async Task QueryAsync_GivenTheOwningTenant_ReturnsTheEntry(CancellationToken cancellationToken)
	{
		var table = await SeededTableAsync(cancellationToken);

		var page = await ReaderFor(table, TenantScope.For("acme")).QueryAsync(new AdminAuditQuery(), cancellationToken);

		await Assert.That(page.Entries.Select(entry => entry.Target)).Contains("acme-order");
	}

	[Test]
	public async Task QueryAsync_GivenAnotherTenant_DoesNotReturnTheEntry(CancellationToken cancellationToken)
	{
		var table = await SeededTableAsync(cancellationToken);

		var page = await ReaderFor(table, TenantScope.For("globex"))
			.QueryAsync(new AdminAuditQuery(), cancellationToken);

		await Assert.That(page.Entries.Select(entry => entry.Target)).DoesNotContain("acme-order");
	}

	// Asserting only the denial would also pass if the predicate hid every row from everyone.
	[Test]
	public async Task QueryAsync_GivenTheSystemScope_ReturnsEveryTenantsEntries(CancellationToken cancellationToken)
	{
		var table = await SeededTableAsync(cancellationToken);

		var page = await ReaderFor(table, TenantScope.System).QueryAsync(new AdminAuditQuery(), cancellationToken);
		var targets = page.Entries.Select(entry => entry.Target).ToArray();

		await Assert.That(targets).Contains("acme-order");
		await Assert.That(targets).Contains("globex-order");
	}

	// Fail closed: a row written before the deployment became multi-tenant must look absent to a
	// tenant, not visible to all of them.
	[Test]
	public async Task QueryAsync_GivenAnUnownedRow_IsInvisibleToATenant(CancellationToken cancellationToken)
	{
		var table = await SeededTableAsync(cancellationToken);

		var tenant = await ReaderFor(table, TenantScope.For("acme"))
			.QueryAsync(new AdminAuditQuery(), cancellationToken);
		var system = await ReaderFor(table, TenantScope.System).QueryAsync(new AdminAuditQuery(), cancellationToken);

		await Assert.That(tenant.Entries.Select(entry => entry.Target)).DoesNotContain("unowned-order");
		await Assert.That(system.Entries.Select(entry => entry.Target)).Contains("unowned-order");
	}

	[Test]
	public async Task QueryAsync_PagesWithoutOverfillingAndReportsHasMore(CancellationToken cancellationToken)
	{
		var table = await SeededTableAsync(cancellationToken);
		var reader = ReaderFor(table, TenantScope.System);

		var first = await reader.QueryAsync(new AdminAuditQuery { PageSize = 2 }, cancellationToken);
		var second = await reader.QueryAsync(new AdminAuditQuery { PageSize = 2, Page = 2 }, cancellationToken);

		await Assert.That(first.Entries.Count).IsEqualTo(2);
		await Assert.That(first.HasMore).IsTrue();
		await Assert.That(second.Entries.Count).IsEqualTo(2);
		await Assert.That(second.HasMore).IsFalse();

		// A page boundary that repeats or drops a row is the defect OFFSET paging actually has.
		var overlap = first
			.Entries.Select(entry => entry.Target)
			.Intersect(second.Entries.Select(entry => entry.Target));

		await Assert.That(overlap).IsEmpty();
	}

	[Test]
	public async Task QueryAsync_OrdersNewestFirst(CancellationToken cancellationToken)
	{
		var table = await SeededTableAsync(cancellationToken);

		var page = await ReaderFor(table, TenantScope.System).QueryAsync(new AdminAuditQuery(), cancellationToken);
		var timestamps = page.Entries.Select(entry => entry.TimestampUtc).ToArray();

		await Assert.That(timestamps).IsEquivalentTo(timestamps.OrderByDescending(timestamp => timestamp).ToArray());
	}

	[Test]
	public async Task QueryAsync_FiltersOnFeatureWithinTheScope(CancellationToken cancellationToken)
	{
		var table = await SeededTableAsync(cancellationToken);

		var page = await ReaderFor(table, TenantScope.System)
			.QueryAsync(new AdminAuditQuery { Feature = AdminFeature.ExportEvents }, cancellationToken);

		await Assert.That(page.Entries).IsNotEmpty();
		await Assert.That(page.Entries.All(entry => entry.Feature == AdminFeature.ExportEvents)).IsTrue();
	}

	async Task<string> SeededTableAsync(CancellationToken cancellationToken)
	{
		var table = $"AuditLog_{Guid.NewGuid():N}";
		using SqlServerAdminAuditLogger logger = new(
			Options.Create(
				new SqlServerAdminAuditLoggerOptions { ConnectionString = fixture.ConnectionString, TableName = table }
			),
			Options.Create(new SqlServerEventStoreOptions { ConnectionString = fixture.ConnectionString }),
			NullLogger<SqlServerAdminAuditLogger>.Instance
		);

		var seededAt = DateTimeOffset.UtcNow;

		// Distinct timestamps, because the ordering assertion is otherwise satisfied by chance on a
		// clock whose granularity is coarser than the writes.
		await logger.LogAsync(Entry("acme-order", AdminFeature.ViewEvents, "acme", seededAt), cancellationToken);
		await logger.LogAsync(
			Entry("globex-order", AdminFeature.ViewEvents, "globex", seededAt.AddMilliseconds(100)),
			cancellationToken
		);
		await logger.LogAsync(
			Entry("acme-export", AdminFeature.ExportEvents, "acme", seededAt.AddMilliseconds(200)),
			cancellationToken
		);
		await logger.LogAsync(
			Entry("unowned-order", AdminFeature.ViewEvents, tenantId: null, seededAt.AddMilliseconds(300)),
			cancellationToken
		);

		return table;
	}

	IAdminAuditEntryReader ReaderFor(string tableName, TenantScope scope) =>
		new SqlServerAdminAuditEntryReader(
			Options.Create(
				new SqlServerAdminAuditLoggerOptions
				{
					ConnectionString = fixture.ConnectionString,
					TableName = tableName,
				}
			),
			Options.Create(new SqlServerEventStoreOptions { ConnectionString = fixture.ConnectionString }),
			new FixedCurrentTenant(scope)
		);

	static AdminAuditEntry Entry(string target, AdminFeature feature, string? tenantId, DateTimeOffset timestamp) =>
		new(timestamp, feature, "Read", "an-administrator", target, Succeeded: true, Details: null)
		{
			TenantId = tenantId,
		};

	sealed class FixedCurrentTenant(TenantScope scope) : ICurrentTenant
	{
		public TenantScope Current => scope;
	}
}
