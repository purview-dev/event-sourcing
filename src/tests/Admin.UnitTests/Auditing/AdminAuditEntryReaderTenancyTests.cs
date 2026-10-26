using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Admin.API.Auditing;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Security;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Admin.Auditing;

/// <summary>
/// The tenant boundary on the audit read path.
/// </summary>
/// <remarks>
/// <para>
/// An audit trail that leaks across tenants discloses the principals, targets and timings of another
/// tenant's administration - who looked at what, and when. That is arguably worse than disclosing
/// the data itself, and it is invisible to every test that only checks the entries come back.
/// </para>
/// <para>
/// <strong>One sink, two scopes.</strong> Both readers here read the <em>same</em>
/// <see cref="InMemoryAdminAuditLogger"/>; only the <see cref="ICurrentTenant"/> differs. A test that
/// gave each scope its own sink would pass while proving nothing, which is the failure mode this
/// repository has hit repeatedly.
/// </para>
/// </remarks>
public sealed class AdminAuditEntryReaderTenancyTests
{
	[Test]
	public async Task QueryAsync_GivenTheOwningTenant_ReturnsTheEntry()
	{
		var sink = await SeededSinkAsync();

		var page = await ReaderFor(sink, TenantScope.For("acme"))
			.QueryAsync(new AdminAuditQuery(), CancellationToken.None);

		await Assert.That(page.Entries.Select(entry => entry.Target)).Contains("acme-order");
	}

	[Test]
	public async Task QueryAsync_GivenAnotherTenant_DoesNotReturnTheEntry()
	{
		var sink = await SeededSinkAsync();

		var page = await ReaderFor(sink, TenantScope.For("globex"))
			.QueryAsync(new AdminAuditQuery(), CancellationToken.None);

		await Assert.That(page.Entries.Select(entry => entry.Target)).DoesNotContain("acme-order");
	}

	// Asserting only the denial would also pass if the filter hid the entry from everyone, which is
	// a different bug with the same symptom in a one-sided test.
	[Test]
	public async Task QueryAsync_GivenTheSystemScope_ReturnsEveryTenantsEntries()
	{
		var sink = await SeededSinkAsync();

		var page = await ReaderFor(sink, TenantScope.System).QueryAsync(new AdminAuditQuery(), CancellationToken.None);

		var targets = page.Entries.Select(entry => entry.Target).ToArray();

		await Assert.That(targets).Contains("acme-order");
		await Assert.That(targets).Contains("globex-order");
	}

	// Fail closed. An entry written before a deployment became multi-tenant, or by a path that
	// forgot the stamp, must look absent to a tenant rather than visible to all of them - otherwise
	// one missed stamp turns into silent cross-tenant disclosure.
	[Test]
	public async Task QueryAsync_GivenAnUnownedEntry_IsInvisibleToATenantAndVisibleToSystem()
	{
		var sink = await SeededSinkAsync();

		var tenant = await ReaderFor(sink, TenantScope.For("acme"))
			.QueryAsync(new AdminAuditQuery(), CancellationToken.None);
		var system = await ReaderFor(sink, TenantScope.System)
			.QueryAsync(new AdminAuditQuery(), CancellationToken.None);

		await Assert.That(tenant.Entries.Select(entry => entry.Target)).DoesNotContain("unowned-order");
		await Assert.That(system.Entries.Select(entry => entry.Target)).Contains("unowned-order");
	}

	[Test]
	public async Task QueryAsync_FiltersApplyWithinTheTenantScope()
	{
		var sink = await SeededSinkAsync();
		var reader = ReaderFor(sink, TenantScope.System);

		var page = await reader.QueryAsync(
			new AdminAuditQuery { Feature = AdminFeature.ExportEvents },
			CancellationToken.None
		);

		await Assert.That(page.Entries).IsNotEmpty();
		await Assert.That(page.Entries.All(entry => entry.Feature == AdminFeature.ExportEvents)).IsTrue();
	}

	// HasMore rather than a total count: the caller needs to know whether to page, and counting an
	// append-only table on every page costs a scan for a number that is stale on arrival.
	[Test]
	public async Task QueryAsync_GivenMoreEntriesThanThePage_ReportsHasMoreWithoutOverfilling()
	{
		var sink = await SeededSinkAsync();
		var reader = ReaderFor(sink, TenantScope.System);

		var page = await reader.QueryAsync(new AdminAuditQuery { PageSize = 2 }, CancellationToken.None);

		await Assert.That(page.Entries.Count).IsEqualTo(2);
		await Assert.That(page.HasMore).IsTrue();
	}

	[Test]
	public async Task QueryAsync_ClampsAnUnboundedPageSize()
	{
		var sink = await SeededSinkAsync();

		var page = await ReaderFor(sink, TenantScope.System)
			.QueryAsync(new AdminAuditQuery { PageSize = int.MaxValue }, CancellationToken.None);

		await Assert.That(page.PageSize).IsEqualTo(AdminAuditQuery.MaximumPageSize);
	}

	// The default wiring must not hand back a reader over an in-memory sink a durable logger has
	// replaced: that would answer "nothing was audited" about a trail sitting in a table.
	[Test]
	public async Task AddAdminAuditContextStamping_GivenADurableSinkWithNoReader_FailsLoudly()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddEventSourcing();
		services.AddKeyedSingleton<IAdminAuditLogger>(AdminAuditKeys.Sink, new DurableSinkWithNoReader());
		services.AddAdminAuditContextStamping();

		await using var provider = services.BuildServiceProvider();

		await Assert
			.That(() => provider.GetRequiredService<IAdminAuditEntryReader>())
			.Throws<InvalidOperationException>();
	}

	static async Task<InMemoryAdminAuditLogger> SeededSinkAsync()
	{
		InMemoryAdminAuditLogger sink = new();

		await sink.LogAsync(Entry("acme-order", AdminFeature.ViewEvents, tenantId: "acme"), CancellationToken.None);
		await sink.LogAsync(Entry("globex-order", AdminFeature.ViewEvents, tenantId: "globex"), CancellationToken.None);
		await sink.LogAsync(Entry("acme-export", AdminFeature.ExportEvents, tenantId: "acme"), CancellationToken.None);
		await sink.LogAsync(Entry("unowned-order", AdminFeature.ViewEvents, tenantId: null), CancellationToken.None);

		return sink;
	}

	static IAdminAuditEntryReader ReaderFor(InMemoryAdminAuditLogger sink, TenantScope scope) =>
		new InMemoryAdminAuditEntryReader(sink, new FixedCurrentTenant(scope));

	static AdminAuditEntry Entry(string target, AdminFeature feature, string? tenantId) =>
		new(DateTimeOffset.UtcNow, feature, "Read", "an-administrator", target, Succeeded: true, Details: null)
		{
			TenantId = tenantId,
		};

	sealed class FixedCurrentTenant(TenantScope scope) : ICurrentTenant
	{
		public TenantScope Current => scope;
	}

	sealed class DurableSinkWithNoReader : IAdminAuditLogger
	{
		public Task LogAsync(AdminAuditEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
	}
}
