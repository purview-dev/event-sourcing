using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Admin.Abstractions.Models;
using Purview.EventSourcing.Admin.Abstractions.Queries;
using Purview.EventSourcing.Admin.Abstractions.Services;
using Purview.EventSourcing.Admin.Security;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Outbox;
using Purview.EventSourcing.Services;

namespace Purview.EventSourcing.Admin.API;

public sealed class AdminOperationalEndpointsTests
{
	[Test]
	public async Task Capabilities_WhenFeatureEnabled_ReturnsCapabilityContract(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync(configureAdmin: static options =>
			options.Features.ViewCapabilities = true
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/capabilities", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
		var root = json!.RootElement;
		await Assert.That(root.GetProperty("transactionGuarantee").GetInt32()).IsEqualTo(1);
		await Assert.That(root.GetProperty("supportsEventStreams").GetBoolean()).IsTrue();
		await Assert.That(root.GetProperty("supportsQueries").GetBoolean()).IsTrue();
	}

	[Test]
	public async Task Capabilities_WhenFeatureDisabled_ReturnsNotFound(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/capabilities", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task Health_WhenFeatureEnabled_ReturnsReadinessSummary(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync(configureAdmin: static options =>
			options.Features.ViewCapabilities = true
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/health", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
		var root = json!.RootElement;
		await Assert.That(root.GetProperty("status").GetString()).IsEqualTo("Ready");
		await Assert.That(root.GetProperty("supportsTransactionalOutbox").GetBoolean()).IsFalse();
	}

	[Test]
	public async Task Capabilities_GivenDeniedPermission_ReturnsForbidden(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Features.ViewCapabilities = true,
			permissionProvider: new DenyCapabilitiesPermissionProvider()
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/capabilities", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task Capabilities_GivenEnabledFeature_AuditsPrivilegedRead(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync(configureAdmin: static options =>
			options.Features.ViewCapabilities = true
		);
		var client = host.Client;

		await client.GetAsync("/admin/api/capabilities", cancellationToken);
		await client.GetAsync("/admin/api/health", cancellationToken);

		var auditLogger = host.App.Services.GetRequiredService<IAdminAuditLogger>();
		var audit = (InMemoryAdminAuditLogger)auditLogger;
		await Assert.That(audit.Entries.Count).IsEqualTo(2);
		await Assert.That(audit.Entries.All(static entry => entry.Feature == AdminFeature.ViewCapabilities)).IsTrue();
		await Assert.That(audit.Entries.All(static entry => entry.Succeeded)).IsTrue();
	}

	[Test]
	public async Task PoisonedOutbox_WhenEnabledAndStoreRegistered_ReturnsPoisonedMessages(
		CancellationToken cancellationToken
	)
	{
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Features.ViewPoisonedOutbox = true,
			configureServices: static services => services.AddSingleton<IOutboxStore>(new StubPoisonedOutboxStore())
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/outbox/poisoned", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
		var root = json!.RootElement;
		await Assert.That(root.GetProperty("count").GetInt32()).IsEqualTo(1);
		var item = root.GetProperty("items")[0];
		await Assert.That(item.GetProperty("eventType").GetString()).IsEqualTo("OrderCreated");
		await Assert.That(item.GetProperty("lastError").GetString()).IsNotEmpty();
	}

	[Test]
	public async Task PoisonedOutbox_WhenNoOutboxStoreRegistered_ReturnsNotFound(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync(configureAdmin: static options =>
			options.Features.ViewPoisonedOutbox = true
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/outbox/poisoned", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task PoisonedOutbox_WhenFeatureDisabled_ReturnsNotFound(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/outbox/poisoned", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task PoisonedOutbox_GivenDeniedPermission_ReturnsForbidden(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Features.ViewPoisonedOutbox = true,
			permissionProvider: new DenyPoisonedOutboxPermissionProvider()
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/outbox/poisoned", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task ExportEvents_GivenCompleteStream_TruncationHeaderFalse(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/aggregates/order/order-1/events/export", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Headers.GetValues("Purview-Event-Export-Truncated").Single()).IsEqualTo("false");
	}

	[Test]
	public async Task ExportEvents_GivenTruncatedStream_SetsTruncationHeader(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Projections.MaxVersionsPerQuery = 2,
			configureServices: static services =>
				services.AddSingleton<IAdminEventQueryService>(new PagingEventQueryService())
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/aggregates/order/order-1/events/export", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Headers.GetValues("Purview-Event-Export-Truncated").Single()).IsEqualTo("true");

		var text = await response.Content.ReadAsStringAsync(cancellationToken);
		var lines = text.Split(['\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		await Assert.That(lines.Length).IsEqualTo(2);
	}

	[Test]
	public async Task Manifest_WhenEnabledAndProviderRegistered_ReturnsManifestAndStatus(
		CancellationToken cancellationToken
	)
	{
		const string manifestJson = /*lang=json,strict*/
			"""{"formatVersion":1,"aggregates":[]}""";
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Features.ViewManifest = true,
			configureServices: static services =>
				services.AddEventContractManifest(1, manifestJson, baselineJson: manifestJson)
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/manifest", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
		var root = json!.RootElement;
		await Assert.That(root.GetProperty("formatVersion").GetInt32()).IsEqualTo(1);
		await Assert.That(root.GetProperty("compatibilityStatus").GetInt32()).IsEqualTo(1);
	}

	[Test]
	public async Task Manifest_WhenNoProviderRegistered_ReturnsNotFound(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync(configureAdmin: static options =>
			options.Features.ViewManifest = true
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/manifest", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task Manifest_WhenFeatureDisabled_ReturnsNotFound(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/manifest", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task Manifest_GivenDeniedPermission_ReturnsForbidden(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Features.ViewManifest = true,
			permissionProvider: new DenyManifestPermissionProvider()
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/manifest", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	sealed class DenyManifestPermissionProvider : IAdminPermissionProvider
	{
		public Task<IReadOnlyList<AdminPermission>> GetPermissionsAsync(
			System.Security.Claims.ClaimsPrincipal user,
			CancellationToken cancellationToken
		) => Task.FromResult<IReadOnlyList<AdminPermission>>([new(AdminFeature.ViewManifest, null, Allowed: false)]);
	}

	[Test]
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Maintainability",
		"CA1506:Avoid excessive class coupling",
		Justification = "Endpoint test that wires the Admin host and asserts the operational response."
	)]
	public async Task UnknownEvents_WhenEnabled_ReportsUnresolvableEventNames(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Features.ViewUnknownEvents = true,
			configureServices: static services =>
			{
				services.AddSingleton<IAggregateTypeRegistry>(new StubTypeRegistry("order"));
				services.AddSingleton<IAggregateEventNameMapper>(
					new StubEventNameMapper(known: ["Known.OrderCreated"])
				);
			}
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/aggregates/order/order-1/events/unknown", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
		var root = json!.RootElement;
		await Assert.That(root.GetProperty("aggregateId").GetString()).IsEqualTo("order-1");
		await Assert.That(root.GetProperty("totalEvents").GetInt32()).IsEqualTo(1);
		var names = root.GetProperty("unknownEventNames").EnumerateArray().Select(x => x.GetString()!).ToArray();
		await Assert.That(names).IsEquivalentTo(["OrderCreatedEvent"]);
	}

	[Test]
	public async Task UnknownEvents_GivenUnregisteredAggregate_ReturnsNotFound(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Features.ViewUnknownEvents = true,
			configureServices: static services =>
			{
				services.AddSingleton<IAggregateTypeRegistry>(new StubTypeRegistry("other"));
				services.AddSingleton<IAggregateEventNameMapper>(new StubEventNameMapper());
			}
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/aggregates/order/order-1/events/unknown", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task UnknownEvents_WhenFeatureDisabled_ReturnsNotFound(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/aggregates/order/order-1/events/unknown", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	sealed class StubTypeRegistry(string resolvableName) : IAggregateTypeRegistry
	{
		public bool TryResolve(
			string aggregateTypeName,
			[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Type? aggregateType
		)
		{
			if (StringComparer.Ordinal.Equals(aggregateTypeName, resolvableName))
			{
				aggregateType = typeof(Aggregates.Persistence.PersistenceAggregate);
				return true;
			}

			aggregateType = null;
			return false;
		}
	}

	sealed class StubEventNameMapper(HashSet<string>? known = null) : IAggregateEventNameMapper
	{
		public string GetName<T>(object aggregateEvent)
			where T : IAggregate => throw new NotSupportedException();

		public string GetName<T>(Type aggregateEventType)
			where T : IAggregate => throw new NotSupportedException();

		public string? GetTypeName<T>(string eventTypeName)
			where T : IAggregate => throw new NotSupportedException();

		public string? GetTypeName(string eventTypeName) => known?.Contains(eventTypeName) == true ? "KnownType" : null;

		public string InitializeAggregate<T>()
			where T : class, IAggregate, new() => throw new NotSupportedException();
	}

	sealed class PagingEventQueryService : IAdminEventQueryService
	{
		public Task<PagedResult<EventEnvelopeResponse>?> GetRangeAsync(
			string aggregateType,
			string aggregateId,
			EventRangeQuery query,
			CancellationToken cancellationToken
		)
		{
			const int total = 6;
			var pageSize = Math.Max(1, query.PageSize);
			var page = Math.Max(1, query.Page);
			List<EventEnvelopeResponse> items = [];
			for (var i = 0; i < pageSize; i++)
			{
				var version = ((page - 1) * pageSize) + i + 1;
				if (version > total)
					break;

				items.Add(
					new EventEnvelopeResponse(
						aggregateType,
						aggregateId,
						new EventMetadataResponse(
							1,
							new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(version),
							"OrderCreatedEvent",
							version,
							null,
							null,
							null,
							null
						),
						JsonDocument.Parse("{}").RootElement.Clone()
					)
				);
			}

			return Task.FromResult<PagedResult<EventEnvelopeResponse>?>(
				new PagedResult<EventEnvelopeResponse>(items, page, pageSize, total)
			);
		}
	}

	sealed class DenyCapabilitiesPermissionProvider : IAdminPermissionProvider
	{
		public Task<IReadOnlyList<AdminPermission>> GetPermissionsAsync(
			System.Security.Claims.ClaimsPrincipal user,
			CancellationToken cancellationToken
		) =>
			Task.FromResult<IReadOnlyList<AdminPermission>>([new(AdminFeature.ViewCapabilities, null, Allowed: false)]);
	}

	sealed class DenyPoisonedOutboxPermissionProvider : IAdminPermissionProvider
	{
		public Task<IReadOnlyList<AdminPermission>> GetPermissionsAsync(
			System.Security.Claims.ClaimsPrincipal user,
			CancellationToken cancellationToken
		) =>
			Task.FromResult<IReadOnlyList<AdminPermission>>([
				new(AdminFeature.ViewPoisonedOutbox, null, Allowed: false),
			]);
	}

	sealed class StubPoisonedOutboxStore : IOutboxStore
	{
		public Task<int> EnqueueAsync(OutboxEnvelope message, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task<IReadOnlyList<OutboxEnvelope>> ClaimNextBatchAsync(
			string leaseOwner,
			DateTimeOffset leaseUntil,
			int batchSize,
			CancellationToken cancellationToken
		) => throw new NotSupportedException();

		public Task CompleteAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();

		public Task MarkFailedAsync(
			string id,
			string errorMessage,
			DateTimeOffset nextAttemptUtc,
			CancellationToken cancellationToken
		) => throw new NotSupportedException();

		public Task MarkPoisonedAsync(string id, string errorMessage, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task<int> CleanupAsync(TimeSpan retention, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task<IReadOnlyList<OutboxEnvelope>> GetPoisonedAsync(
			int skip,
			int take,
			CancellationToken cancellationToken
		) =>
			Task.FromResult<IReadOnlyList<OutboxEnvelope>>([
				new OutboxEnvelope(
					"poison-1",
					"OrderAggregate",
					"order-1",
					"OrderCreated",
					"{}",
					IdempotencyKey: null,
					CorrelationId: null,
					CreatedUtc: DateTimeOffset.UtcNow
				)
				{
					State = OutboxState.Poisoned,
					AttemptCount = 5,
					LastError = "handler failed",
				},
			]);
	}
}
