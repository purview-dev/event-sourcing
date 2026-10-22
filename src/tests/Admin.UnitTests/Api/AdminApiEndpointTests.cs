using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Queries;
using Purview.EventSourcing.Admin.Services;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Purview.EventSourcing.Admin.API;

public sealed class AdminAPIEndpointTests
{
	[Test]
	public async Task HostPolicyOverride_IsEnforced(CancellationToken cancellationToken)
	{
		const string hostPolicy = "HostAdminPolicy";
		await using var host = await AdminTestHost.CreateAsync(
			endpoints => endpoints.RequirePolicy(AdminFeature.SearchAggregates, hostPolicy),
			authorization => authorization.AddPolicy(hostPolicy, policy => policy.RequireAssertion(_ => false))
		);

		var response = await host.Client.PostAsJsonAsync(
			"/admin/api/aggregates/search",
			new { page = 1, pageSize = 25 },
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task HostEndpointConvention_ReceivesFeatureAndChangesMetadata()
	{
		await using var host = await AdminTestHost.CreateAsync(endpoints =>
			endpoints.EndpointConvention = (feature, endpoint) =>
				endpoint.WithMetadata(new HostConventionMetadata(feature))
		);

		var dataSources = ((IEndpointRouteBuilder)host.App).DataSources;
		var endpoint = dataSources
			.SelectMany(source => source.Endpoints)
			.Single(candidate => candidate.DisplayName?.Contains("SearchAggregates", StringComparison.Ordinal) == true);

		await Assert
			.That(endpoint.Metadata.GetMetadata<HostConventionMetadata>()?.Feature)
			.IsEqualTo(AdminFeature.SearchAggregates);
	}

	[Test]
	public async Task EventRange_WithoutPayloadPermission_ReturnsMetadataWithRedactedPayload(
		CancellationToken cancellationToken
	)
	{
		await using var host = await AdminTestHost.CreateAsync(
			permissionProvider: new MetadataOnlyPermissionProvider()
		);

		var response = await host.Client.GetAsync(
			"/admin/api/aggregates/Order/order-1/events?page=1&pageSize=25",
			cancellationToken
		);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var envelope = json!.RootElement.GetProperty("items")[0];
		await Assert.That(envelope.GetProperty("metadata").GetProperty("eventType").GetString()).IsNotEmpty();
		await Assert.That(envelope.GetProperty("payload").ValueKind).IsEqualTo(JsonValueKind.Null);
	}

	[Test]
	public async Task Export_WithoutPayloadPermission_IsForbidden(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync(
			permissionProvider: new MetadataOnlyPermissionProvider(includeExport: true)
		);

		var response = await host.Client.GetAsync(
			"/admin/api/aggregates/Order/order-1/events/export?page=1&pageSize=25",
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task OpenAPIDocument_ContainsOnlyAdminPathsAndBearerSecurity(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync("/openapi/admin.json", cancellationToken);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var paths = json!.RootElement.GetProperty("paths");
		await Assert.That(paths.EnumerateObject().Any(p => p.Name == "/admin/api/aggregates/search")).IsTrue();
		await Assert
			.That(paths.EnumerateObject().Any(p => p.Name == "/admin/api/aggregates/{aggregateType}/{aggregateId}"))
			.IsTrue();
		await Assert
			.That(paths.EnumerateObject().All(p => p.Name.StartsWith("/admin/api", StringComparison.Ordinal)))
			.IsTrue();
		await Assert
			.That(
				json.RootElement.GetProperty("components")
					.GetProperty("securitySchemes")
					.TryGetProperty("Bearer", out _)
			)
			.IsTrue();
	}

	[Test]
	public async Task SearchAggregates_InvalidPage_ReturnsValidationProblem(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.PostAsJsonAsync(
			"/admin/api/aggregates/search",
			new { page = 0, pageSize = 25 },
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
	}

	[Test]
	public async Task SearchAggregates_InvertedTimeRange_ReturnsValidationProblem(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.PostAsJsonAsync(
			"/admin/api/aggregates/search",
			new { fromUtc = "2024-02-01T00:00:00Z", toUtc = "2024-01-01T00:00:00Z" },
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task SearchAggregates_ValidRequest_ReturnsOk(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.PostAsJsonAsync(
			"/admin/api/aggregates/search",
			new
			{
				aggregateType = "order",
				aggregateId = "order-1",
				page = 1,
				pageSize = 25,
			},
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
		var root = json!.RootElement;
		await Assert.That(root.GetProperty("totalCount").GetInt32()).IsEqualTo(1);
		var item = root.GetProperty("items")[0];
		await Assert.That(item.GetProperty("aggregateType").GetString()).IsEqualTo("order");
		await Assert.That(item.GetProperty("aggregateId").GetString()).IsEqualTo("order-1");
		await Assert.That(item.GetProperty("currentVersion").GetInt64()).IsEqualTo(3);
		await Assert.That(item.GetProperty("isDeleted").GetBoolean()).IsFalse();
	}

	[Test]
	public async Task SearchAggregates_PageSizeClampedToMax(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.PostAsJsonAsync(
			"/admin/api/aggregates/search",
			new { pageSize = 100000 },
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(host.AggregateQueryService.LastQuery?.PageSize).IsEqualTo(200);
	}

	[Test]
	public async Task GetAggregate_ExistingAggregate_ReturnsOk(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/aggregates/order/order-1", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
		await Assert.That(json.GetProperty("aggregateType").GetString()).IsEqualTo("order");
		await Assert.That(json.GetProperty("aggregateId").GetString()).IsEqualTo("order-1");
		await Assert.That(json.GetProperty("currentVersion").GetInt64()).IsEqualTo(3);
		await Assert.That(json.GetProperty("isDeleted").GetBoolean()).IsFalse();
	}

	[Test]
	public async Task GetAggregateEventRange_InvalidPage_ReturnsValidationProblem(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync(
			"/admin/api/aggregates/OrderAggregate/order-1/events?page=0",
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task GetAggregateEventRange_NonPositiveVersion_ReturnsValidationProblem(
		CancellationToken cancellationToken
	)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync(
			"/admin/api/aggregates/OrderAggregate/order-1/events?versionFrom=0",
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task GetAggregateEventRange_ValidRequest_ReturnsOk(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/aggregates/order/order-1/events", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
		var root = json!.RootElement;
		await Assert.That(root.GetProperty("totalCount").GetInt32()).IsEqualTo(1);
		var envelope = root.GetProperty("items")[0];
		await Assert.That(envelope.GetProperty("aggregateId").GetString()).IsEqualTo("order-1");
		await Assert.That(envelope.GetProperty("metadata").GetProperty("version").GetInt64()).IsEqualTo(1);
		await Assert
			.That(envelope.GetProperty("metadata").GetProperty("eventType").GetString())
			.IsEqualTo("OrderCreatedEvent");
		await Assert.That(envelope.GetProperty("metadata").GetProperty("schemaVersion").GetInt32()).IsEqualTo(1);
		await Assert
			.That(envelope.GetProperty("payload").GetProperty("customerId").GetString())
			.IsEqualTo("customer-1");
	}

	[Test]
	public async Task ProjectionAtVersion_ValidRequest_ReturnsProjectedState(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync(
			"/admin/api/aggregates/order/order-1/projection?version=1",
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
		var root = json!.RootElement;
		await Assert.That(root.GetProperty("projectedVersion").GetInt64()).IsEqualTo(1);
		await Assert.That(root.GetProperty("provenance").GetProperty("appliedCount").GetInt32()).IsEqualTo(1);
		await Assert
			.That(root.GetProperty("state").GetProperty("event_1").GetProperty("eventType").GetString())
			.IsEqualTo("OrderCreatedEvent");
	}

	[Test]
	public async Task ProjectionAtVersion_InvalidVersion_ReturnsValidationProblem(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync(
			"/admin/api/aggregates/OrderAggregate/order-1/projection?version=0",
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task ProjectionAtTime_NonUtcOffset_ReturnsValidationProblem(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync(
			"/admin/api/aggregates/OrderAggregate/order-1/projection/time?asOfUtc=2024-01-01T00:00:00%2B05:00",
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task ProjectionAtTime_ValidRequest_ReturnsProjectedState(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync(
			"/admin/api/aggregates/order/order-1/projection/time?asOfUtc=2024-01-01T00%3A00%3A00Z",
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
		var root = json!.RootElement;
		await Assert.That(root.GetProperty("projectedVersion").GetInt64()).IsEqualTo(1);
		await Assert.That(root.GetProperty("provenance").GetProperty("appliedCount").GetInt32()).IsEqualTo(1);
		await Assert
			.That(root.GetProperty("state").GetProperty("event_1").GetProperty("eventType").GetString())
			.IsEqualTo("OrderCreatedEvent");
	}

	[Test]
	public async Task ExportEvents_ReturnsNdjsonStream(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/aggregates/order/order-1/events/export", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/x-ndjson");

		var text = await response.Content.ReadAsStringAsync(cancellationToken);
		var lines = text.Split(['\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
		await Assert.That(lines.Count).IsEqualTo(1);
		using var doc = JsonDocument.Parse(lines[0]);
		await Assert.That(doc.RootElement.GetProperty("aggregateId").GetString()).IsEqualTo("order-1");
		await Assert
			.That(doc.RootElement.GetProperty("metadata").GetProperty("eventType").GetString())
			.IsEqualTo("OrderCreatedEvent");
	}
}

sealed class AdminTestHost : IAsyncDisposable
{
	AdminTestHost(WebApplication app, HttpClient client, RecordingAggregateQueryService aggregateQueryService)
	{
		App = app;
		Client = client;
		AggregateQueryService = aggregateQueryService;
	}

	public WebApplication App { get; }

	public HttpClient Client { get; }

	public RecordingAggregateQueryService AggregateQueryService { get; }

	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Maintainability",
		"CA1506:Avoid excessive class coupling",
		Justification = "Test host builder that wires the Admin API endpoints and their service dependencies."
	)]
	public static async Task<AdminTestHost> CreateAsync(
		Action<AdminEndpointOptions>? configureEndpoints = null,
		Action<AuthorizationBuilder>? configureAuthorization = null,
		IAdminPermissionProvider? permissionProvider = null,
		Action<AdminPortalOptions>? configureAdmin = null,
		Action<IServiceCollection>? configureServices = null
	)
	{
		var builder = WebApplication.CreateBuilder();
		builder.Logging.ClearProviders();

		builder.AddPurviewEventSourcingAdminAPI(options =>
		{
			options.Features.ExportEvents = true;
			configureAdmin?.Invoke(options);
		});
		builder.AddPurviewEventSourcingAdminOpenAPI();
		builder
			.Services.AddAuthentication(TestAuthHandler.SchemeName)
			.AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
		var authorization = builder.Services.AddAuthorizationBuilder().AddPurviewEventSourcingAdminPolicies();
		configureAuthorization?.Invoke(authorization);
		builder.Services.AddPurviewEventSourcingAdminSecurity(permissionProvider ?? new AllowAllPermissionProvider());

		RecordingAggregateQueryService aggregateQueryService = new();
		builder.Services.AddSingleton<IAdminAggregateQueryService>(aggregateQueryService);
		builder.Services.AddSingleton<IAdminEventQueryService, RecordingEventQueryService>();
		builder.Services.AddSingleton<IAdminProjectionService, RecordingProjectionService>();
		builder.Services.AddSingleton<IEventStoreCapabilitiesProvider>(new TestCapabilitiesProvider());
		configureServices?.Invoke(builder.Services);

		var app = builder.Build();
		app.MapPurviewEventSourcingAdminAPI(configureEndpoints: configureEndpoints);
		app.MapOpenApi();

		app.Urls.Clear();
		app.Urls.Add("http://127.0.0.1:0");
		await app.StartAsync();

		var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
		var baseAddress = addresses!.Addresses.First();

		HttpClient client = new() { BaseAddress = new Uri(baseAddress) };
		return new AdminTestHost(app, client, aggregateQueryService);
	}

	public ValueTask DisposeAsync()
	{
		Client.Dispose();
		return App.DisposeAsync();
	}
}

sealed record HostConventionMetadata(AdminFeature Feature);

sealed class TestAuthHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	public const string SchemeName = "Test";

	protected override Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		ClaimsPrincipal principal = new(new ClaimsIdentity(SchemeName));
		return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
	}
}

sealed class TestCapabilitiesProvider : IEventStoreCapabilitiesProvider
{
	static readonly EventStoreCapabilities Capabilities = new(
		EventStoreTransactionGuarantee.Atomic,
		SupportsEventStreams: true,
		SupportsSnapshots: true,
		SnapshotSchemaVersioning: SnapshotSchemaSupport.Versioned,
		PreservedMetadata: PreservedEventMetadata.All,
		SupportsQueries: true,
		SupportsIdempotencyMarkers: true,
		Concurrency: ConcurrencyGuarantee.Optimistic,
		[]
	);

	public EventStoreCapabilities GetCapabilities() => Capabilities;
}

sealed class AllowAllPermissionProvider : IAdminPermissionProvider
{
	static readonly IReadOnlyList<AdminPermission> Permissions =
	[
		new(AdminFeature.SearchAggregates, null, Allowed: true),
		new(AdminFeature.ViewAggregate, null, Allowed: true),
		new(AdminFeature.ViewEvents, null, Allowed: true),
		new(AdminFeature.ViewEventPayloads, null, Allowed: true),
		new(AdminFeature.ProjectPointInTime, null, Allowed: true),
		new(AdminFeature.ExportEvents, null, Allowed: true),
		new(AdminFeature.ViewCapabilities, null, Allowed: true),
		new(AdminFeature.ViewPoisonedOutbox, null, Allowed: true),
		new(AdminFeature.ViewManifest, null, Allowed: true),
		new(AdminFeature.ViewUnknownEvents, null, Allowed: true),
		new(AdminFeature.ViewSnapshot, null, Allowed: true),
		new(AdminFeature.RebuildSnapshot, null, Allowed: true),
	];

	public Task<IReadOnlyList<AdminPermission>> GetPermissionsAsync(
		ClaimsPrincipal user,
		CancellationToken cancellationToken
	) => Task.FromResult(Permissions);
}

sealed class MetadataOnlyPermissionProvider(bool includeExport = false) : IAdminPermissionProvider
{
	public Task<IReadOnlyList<AdminPermission>> GetPermissionsAsync(
		ClaimsPrincipal user,
		CancellationToken cancellationToken
	) =>
		Task.FromResult<IReadOnlyList<AdminPermission>>(
			includeExport
				?
				[
					new(AdminFeature.ViewEvents, null, Allowed: true),
					new(AdminFeature.ExportEvents, null, Allowed: true),
				]
				: [new(AdminFeature.ViewEvents, null, Allowed: true)]
		);
}

sealed class RecordingAggregateQueryService : IAdminAggregateQueryService
{
	public AggregateSearchQuery? LastQuery { get; private set; }

	public Task<PagedResult<AggregateSummaryResponse>> SearchAsync(
		AggregateSearchQuery query,
		CancellationToken cancellationToken
	)
	{
		LastQuery = query;
		AggregateSummaryResponse item = new(
			query.AggregateType ?? "order",
			query.AggregateId ?? "order-1",
			3,
			new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
			new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
			false,
			false
		);
		return Task.FromResult(new PagedResult<AggregateSummaryResponse>([item], query.Page, query.PageSize, 1));
	}

	public Task<AggregateSummaryResponse?> GetAsync(
		string aggregateType,
		string aggregateId,
		CancellationToken cancellationToken
	) =>
		Task.FromResult<AggregateSummaryResponse?>(
			new AggregateSummaryResponse(
				aggregateType,
				aggregateId,
				3,
				new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
				new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
				false,
				false
			)
		);
}

sealed class RecordingEventQueryService : IAdminEventQueryService
{
	public Task<PagedResult<EventEnvelopeResponse>?> GetRangeAsync(
		string aggregateType,
		string aggregateId,
		EventRangeQuery query,
		CancellationToken cancellationToken
	)
	{
		EventEnvelopeResponse envelope = new(
			aggregateType,
			aggregateId,
			new EventMetadataResponse(
				1,
				new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
				"OrderCreatedEvent",
				1,
				null,
				null,
				null,
				null
			),
			JsonSerializer.SerializeToElement(/*lang=json,strict*/ """{"customerId":"customer-1"}""")
		);
		return Task.FromResult<PagedResult<EventEnvelopeResponse>?>(
			new PagedResult<EventEnvelopeResponse>([envelope], query.Page, query.PageSize, 1)
		);
	}
}

sealed class RecordingProjectionService : IAdminProjectionService
{
	static readonly JsonElement ProjectedState = JsonSerializer.SerializeToElement(/*lang=json,strict*/ """{"event_1":{"eventType":"OrderCreatedEvent","version":1}}""");

	public Task<ProjectionResponse?> ProjectAtVersionAsync(
		string aggregateType,
		string aggregateId,
		long targetVersion,
		CancellationToken cancellationToken
	) =>
		Task.FromResult<ProjectionResponse?>(
			new ProjectionResponse(
				aggregateType,
				aggregateId,
				targetVersion,
				DateTimeOffset.UtcNow,
				ProjectedState,
				new ProjectionProvenance(1, 0, [1], [], string.Empty)
			)
		);

	public Task<ProjectionResponse?> ProjectAtTimeAsync(
		string aggregateType,
		string aggregateId,
		DateTimeOffset targetUtc,
		CancellationToken cancellationToken
	) =>
		Task.FromResult<ProjectionResponse?>(
			new ProjectionResponse(
				aggregateType,
				aggregateId,
				1,
				targetUtc,
				ProjectedState,
				new ProjectionProvenance(1, 0, [1], [], string.Empty)
			)
		);
}
