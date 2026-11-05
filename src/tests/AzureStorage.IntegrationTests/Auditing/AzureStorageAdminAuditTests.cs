using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.AzureStorage;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Fixtures.AzureStorage;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.AzureStorage.Auditing;

/// <summary>
/// The durable admin audit trail against real Azure Table Storage.
/// </summary>
/// <remarks>
/// <para>
/// Before this logger existed an Azure Storage deployment had no durable audit at all: the record of
/// who read which payload died with the process, which in a regulated deployment means there was no
/// record.
/// </para>
/// <para>
/// <strong>The isolation here is structural.</strong> The tenant is the partition key, so another
/// tenant's entries are not filtered out - a tenant-scoped read never addresses them. These tests
/// still assert all three outcomes, because a partition scheme can be wrong in exactly the way a
/// predicate can.
/// </para>
/// </remarks>
[ClassDataSource<TableEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class AzureStorageAdminAuditTests(TableEventStoreFixture fixture)
{
	[Test]
	public async Task QueryAsync_GivenTheOwningTenant_ReturnsTheEntry(CancellationToken cancellationToken)
	{
		var options = await SeededAsync(cancellationToken);

		var page = await ReaderFor(options, TenantScope.For("acme"))
			.QueryAsync(new AdminAuditQuery(), cancellationToken);

		await Assert.That(page.Entries.Select(entry => entry.Target)).Contains("acme-order");
	}

	[Test]
	public async Task QueryAsync_GivenAnotherTenant_DoesNotReturnTheEntry(CancellationToken cancellationToken)
	{
		var options = await SeededAsync(cancellationToken);

		var page = await ReaderFor(options, TenantScope.For("globex"))
			.QueryAsync(new AdminAuditQuery(), cancellationToken);

		await Assert.That(page.Entries.Select(entry => entry.Target)).DoesNotContain("acme-order");
	}

	[Test]
	public async Task QueryAsync_GivenTheSystemScope_ReturnsEveryTenantsEntries(CancellationToken cancellationToken)
	{
		var options = await SeededAsync(cancellationToken);

		var page = await ReaderFor(options, TenantScope.System).QueryAsync(new AdminAuditQuery(), cancellationToken);
		var targets = page.Entries.Select(entry => entry.Target).ToArray();

		await Assert.That(targets).Contains("acme-order");
		await Assert.That(targets).Contains("globex-order");
	}

	// The unowned partition is addressable only by the system scope. A tenant whose identifier
	// happened to produce that partition key would read every unstamped entry in the account.
	[Test]
	public async Task QueryAsync_GivenAnUnownedEntry_IsInvisibleToATenant(CancellationToken cancellationToken)
	{
		var options = await SeededAsync(cancellationToken);

		var tenant = await ReaderFor(options, TenantScope.For("acme"))
			.QueryAsync(new AdminAuditQuery(), cancellationToken);
		var system = await ReaderFor(options, TenantScope.System).QueryAsync(new AdminAuditQuery(), cancellationToken);

		await Assert.That(tenant.Entries.Select(entry => entry.Target)).DoesNotContain("unowned-order");
		await Assert.That(system.Entries.Select(entry => entry.Target)).Contains("unowned-order");
	}

	[Test]
	public async Task LogAsync_RoundTripsEveryRecordedField(CancellationToken cancellationToken)
	{
		var options = await SeededAsync(cancellationToken);

		var page = await ReaderFor(options, TenantScope.For("acme"))
			.QueryAsync(new AdminAuditQuery { Feature = AdminFeature.ExportEvents }, cancellationToken);

		var entry = page.Entries.Single();

		await Assert.That(entry.Action).IsEqualTo("Read");
		await Assert.That(entry.Principal).IsEqualTo("an-administrator");
		await Assert.That(entry.TenantId).IsEqualTo("acme");
		await Assert.That(entry.Target).IsEqualTo("acme-export");
		await Assert.That(entry.Succeeded).IsTrue();
	}

	// Within one partition the inverted row key puts newest first with no sort at all; this is the
	// assertion that would catch the inversion being dropped or applied the wrong way round.
	[Test]
	public async Task QueryAsync_WithinATenant_OrdersNewestFirst(CancellationToken cancellationToken)
	{
		var options = await SeededAsync(cancellationToken);

		var page = await ReaderFor(options, TenantScope.For("acme"))
			.QueryAsync(new AdminAuditQuery(), cancellationToken);
		var timestamps = page.Entries.Select(entry => entry.TimestampUtc).ToArray();

		await Assert.That(timestamps.Length).IsGreaterThan(1);
		await Assert.That(timestamps).IsEquivalentTo(timestamps.OrderByDescending(timestamp => timestamp).ToArray());
	}

	// Two entries written inside one tick must not collide on the row key: Add (not upsert) would
	// fail the second, losing an entry under the failure mode meant to be the safest.
	[Test]
	public async Task LogAsync_GivenEntriesSharingATimestamp_WritesBoth(CancellationToken cancellationToken)
	{
		AzureStorageAdminAuditLoggerOptions options = new()
		{
			ConnectionString = fixture.ConnectionString,
			TableName = $"AuditLog{Guid.NewGuid():N}",
		};

		using var logger = LoggerFor(options);
		var sameInstant = DateTimeOffset.UtcNow;

		await logger.LogAsync(Entry("first", AdminFeature.ViewEvents, "acme", sameInstant), cancellationToken);
		await logger.LogAsync(Entry("second", AdminFeature.ViewEvents, "acme", sameInstant), cancellationToken);

		var page = await ReaderFor(options, TenantScope.For("acme"))
			.QueryAsync(new AdminAuditQuery(), cancellationToken);

		await Assert.That(page.Entries.Count).IsEqualTo(2);
	}

	[Test]
	public async Task QueryAsync_PagesWithoutOverlapAndReportsHasMore(CancellationToken cancellationToken)
	{
		var options = await SeededAsync(cancellationToken);
		var reader = ReaderFor(options, TenantScope.System);

		var first = await reader.QueryAsync(new AdminAuditQuery { PageSize = 2 }, cancellationToken);
		var second = await reader.QueryAsync(new AdminAuditQuery { PageSize = 2, Page = 2 }, cancellationToken);

		await Assert.That(first.Entries.Count).IsEqualTo(2);
		await Assert.That(first.HasMore).IsTrue();
		await Assert.That(second.HasMore).IsFalse();

		var overlap = first
			.Entries.Select(entry => entry.Target)
			.Intersect(second.Entries.Select(entry => entry.Target));

		await Assert.That(overlap).IsEmpty();
	}

	async Task<AzureStorageAdminAuditLoggerOptions> SeededAsync(CancellationToken cancellationToken)
	{
		AzureStorageAdminAuditLoggerOptions options = new()
		{
			ConnectionString = fixture.ConnectionString,
			TableName = $"AuditLog{Guid.NewGuid():N}",
		};

		using var logger = LoggerFor(options);
		var seededAt = DateTimeOffset.UtcNow;

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

		return options;
	}

	AzureStorageAdminAuditLogger LoggerFor(AzureStorageAdminAuditLoggerOptions options) =>
		new(
			Options.Create(options),
			Options.Create(new AzureStorageEventStoreOptions { ConnectionString = fixture.ConnectionString }),
			NullLogger<AzureStorageAdminAuditLogger>.Instance
		);

	IAdminAuditEntryReader ReaderFor(AzureStorageAdminAuditLoggerOptions options, TenantScope scope) =>
		new AzureStorageAdminAuditEntryReader(
			Options.Create(options),
			Options.Create(new AzureStorageEventStoreOptions { ConnectionString = fixture.ConnectionString }),
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
