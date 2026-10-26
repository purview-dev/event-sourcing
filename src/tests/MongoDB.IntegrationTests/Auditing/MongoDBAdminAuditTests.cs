using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.MongoDB;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Fixtures.MongoDB;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.MongoDB.Auditing;

/// <summary>
/// The durable admin audit trail against real MongoDB.
/// </summary>
/// <remarks>
/// <para>
/// Before this logger existed a MongoDB deployment had no durable audit at all: the record of who
/// read which payload died with the process, which in a regulated deployment means there was no
/// record.
/// </para>
/// <para>
/// <strong>Tenancy comes first.</strong> Every test shares one collection and changes only the
/// <see cref="ICurrentTenant"/>, because sharing storage is what the isolation is protecting; a
/// collection per scope would pass while proving nothing.
/// </para>
/// </remarks>
[ClassDataSource<MongoDBEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class MongoDBAdminAuditTests(MongoDBEventStoreFixture fixture)
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

	// Asserting only the denial would also pass if the filter hid every document from everyone.
	[Test]
	public async Task QueryAsync_GivenTheSystemScope_ReturnsEveryTenantsEntries(CancellationToken cancellationToken)
	{
		var options = await SeededAsync(cancellationToken);

		var page = await ReaderFor(options, TenantScope.System).QueryAsync(new AdminAuditQuery(), cancellationToken);
		var targets = page.Entries.Select(entry => entry.Target).ToArray();

		await Assert.That(targets).Contains("acme-order");
		await Assert.That(targets).Contains("globex-order");
	}

	// Fail closed: an unstamped document must look absent to a tenant, not visible to all of them.
	[Test]
	public async Task QueryAsync_GivenAnUnownedDocument_IsInvisibleToATenant(CancellationToken cancellationToken)
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

		await Assert.That(entry.Feature).IsEqualTo(AdminFeature.ExportEvents);
		await Assert.That(entry.Action).IsEqualTo("Read");
		await Assert.That(entry.Principal).IsEqualTo("an-administrator");
		await Assert.That(entry.TenantId).IsEqualTo("acme");
		await Assert.That(entry.Succeeded).IsTrue();
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

	[Test]
	public async Task QueryAsync_OrdersNewestFirst(CancellationToken cancellationToken)
	{
		var options = await SeededAsync(cancellationToken);

		var page = await ReaderFor(options, TenantScope.System).QueryAsync(new AdminAuditQuery(), cancellationToken);
		var timestamps = page.Entries.Select(entry => entry.TimestampUtc).ToArray();

		await Assert.That(timestamps).IsEquivalentTo(timestamps.OrderByDescending(timestamp => timestamp).ToArray());
	}

	// Fail is the default: an action nobody can prove happened is worse than an action that did not.
	[Test]
	public async Task LogAsync_WhenTheEntryCannotBeWritten_FailsByDefault(CancellationToken cancellationToken)
	{
		using MongoDBAdminAuditLogger logger = new(
			new MongoClient("mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=250"),
			Options.Create(new MongoDBAdminAuditLoggerOptions { DatabaseName = "absent" }),
			NullLogger<MongoDBAdminAuditLogger>.Instance
		);

		await Assert
			.That(async () =>
				await logger.LogAsync(
					Entry("x", AdminFeature.ViewEvents, "acme", DateTimeOffset.UtcNow),
					cancellationToken
				)
			)
			.ThrowsException();
	}

	async Task<MongoDBAdminAuditLoggerOptions> SeededAsync(CancellationToken cancellationToken)
	{
		MongoDBAdminAuditLoggerOptions options = new()
		{
			DatabaseName = $"audit{Guid.NewGuid():N}",
			CollectionName = "AdminAuditLog",
		};

		using MongoDBAdminAuditLogger logger = new(
			new MongoClient(fixture.ConnectionString),
			Options.Create(options),
			NullLogger<MongoDBAdminAuditLogger>.Instance
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

		return options;
	}

	IAdminAuditEntryReader ReaderFor(MongoDBAdminAuditLoggerOptions options, TenantScope scope) =>
		new MongoDBAdminAuditEntryReader(
			new MongoClient(fixture.ConnectionString),
			Options.Create(options),
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
