using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Admin.API.Auditing;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Admin.Auditing;

/// <summary>
/// Stamping the tenant and correlation identifier onto audit entries.
/// </summary>
/// <remarks>
/// "Who did this" is incomplete without "to which tenant", and an entry that cannot be tied to the
/// events it caused answers very little. Both are stamped centrally so an endpoint added later
/// cannot forget them.
/// </remarks>
public sealed class ContextStampingAdminAuditLoggerTests
{
	[Test]
	public async Task LogAsync_StampsTheTenantTheOperationActedOn()
	{
		RecordingAuditLogger inner = new();
		var logger = CreateLogger(inner, correlationId: "corr-1");

		using (AmbientCurrentTenant.BeginScope(TenantScope.For("acme")))
			await logger.LogAsync(Entry(), CancellationToken.None);

		await Assert.That(inner.Entries.Single().TenantId).IsEqualTo("acme");
	}

	[Test]
	public async Task LogAsync_StampsTheCorrelationIdentifier()
	{
		RecordingAuditLogger inner = new();
		var logger = CreateLogger(inner, correlationId: "corr-1");

		await logger.LogAsync(Entry(), CancellationToken.None);

		await Assert.That(inner.Entries.Single().CorrelationId).IsEqualTo("corr-1");
	}

	// A single-tenant deployment has no tenant to record, and the system scope is infrastructure
	// rather than a tenant - neither should invent one.
	[Test]
	public async Task LogAsync_GivenTheSystemScope_LeavesTheTenantUnset()
	{
		RecordingAuditLogger inner = new();
		var logger = CreateLogger(inner, correlationId: null);

		using (AmbientCurrentTenant.BeginScope(TenantScope.System))
			await logger.LogAsync(Entry(), CancellationToken.None);

		await Assert.That(inner.Entries.Single().TenantId).IsNull();
	}

	// A caller that already knows better wins: stamping fills gaps, it does not overwrite.
	[Test]
	public async Task LogAsync_GivenAnEntryThatAlreadyCarriesContext_DoesNotOverwriteIt()
	{
		RecordingAuditLogger inner = new();
		var logger = CreateLogger(inner, correlationId: "ambient");

		using (AmbientCurrentTenant.BeginScope(TenantScope.For("acme")))
			await logger.LogAsync(
				Entry() with
				{
					TenantId = "explicit-tenant",
					CorrelationId = "explicit-correlation",
				},
				CancellationToken.None
			);

		var recorded = inner.Entries.Single();

		await Assert.That(recorded.TenantId).IsEqualTo("explicit-tenant");
		await Assert.That(recorded.CorrelationId).IsEqualTo("explicit-correlation");
	}

	// The decorator has to apply to whichever logger the application registered, not just the
	// default - otherwise a deployment with a durable logger silently loses the stamping.
	[Test]
	public async Task AddAdminAuditContextStamping_DecoratesTheRegisteredLogger()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddEventSourcing();
		RecordingAuditLogger inner = new();
		services.AddKeyedSingleton<IAdminAuditLogger>(AdminAuditKeys.Sink, inner);
		services.AddAdminAuditContextStamping();

		await using var provider = services.BuildServiceProvider();
		var resolved = provider.GetRequiredService<IAdminAuditLogger>();

		await Assert.That(resolved).IsTypeOf<ContextStampingAdminAuditLogger>();

		using (AmbientCurrentTenant.BeginScope(TenantScope.For("globex")))
			await resolved.LogAsync(Entry(), CancellationToken.None);

		await Assert.That(inner.Entries.Single().TenantId).IsEqualTo("globex");
	}

	// No sink registered: the in-memory one is the default, so stamping still works rather than
	// failing at start-up. Ordering is no longer a question.
	[Test]
	public async Task AddAdminAuditContextStamping_GivenNoRegisteredSink_FallsBackToInMemory()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddEventSourcing();
		services.AddAdminAuditContextStamping();

		await using var provider = services.BuildServiceProvider();

		await Assert.That(provider.GetRequiredService<IAdminAuditLogger>()).IsTypeOf<ContextStampingAdminAuditLogger>();
		await Assert
			.That(provider.GetRequiredKeyedService<IAdminAuditLogger>(AdminAuditKeys.Sink))
			.IsTypeOf<Security.InMemoryAdminAuditLogger>();
	}

	static ContextStampingAdminAuditLogger CreateLogger(IAdminAuditLogger inner, string? correlationId) =>
		new(inner, new AmbientCurrentTenant(), new FixedCorrelationIdProvider(correlationId));

	static AdminAuditEntry Entry() =>
		new(
			DateTimeOffset.UtcNow,
			AdminFeature.ViewCapabilities,
			"Read",
			"an-administrator",
			Target: null,
			Succeeded: true,
			Details: null
		);

	sealed class RecordingAuditLogger : IAdminAuditLogger
	{
		readonly ConcurrentQueue<AdminAuditEntry> _entries = new();

		public IReadOnlyList<AdminAuditEntry> Entries => [.. _entries];

		public Task LogAsync(AdminAuditEntry entry, CancellationToken cancellationToken)
		{
			_entries.Enqueue(entry);

			return Task.CompletedTask;
		}
	}

	sealed class FixedCorrelationIdProvider(string? correlationId) : IEventStoreCorrelationIdProvider
	{
		public string? GetCorrelationId() => correlationId;
	}
}
