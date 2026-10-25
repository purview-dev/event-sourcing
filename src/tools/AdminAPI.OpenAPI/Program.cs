using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Purview.EventSourcing.Admin.Failures;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Queries;
using Purview.EventSourcing.Admin.Services;
using Purview.Results;

// Exports the Admin API OpenAPI document to a file so it can be committed and used to generate a typed client
// (for example with NSwag). Usage:
//   dotnet run --project src/tools/AdminApi.OpenApi -- <output-path>
// When <output-path> is omitted the document is written to ./src/src/Admin.Client/OpenApi/admin.openapi.json
// relative to the current working directory.

var outputPath = ResolveOutputPath(args);

var builder = WebApplication.CreateBuilder(args);
builder.AddPurviewEventSourcingAdminAPI(options =>
{
	// The exported document describes the full Admin API surface, so all feature-gated endpoints are enabled.
	options.Features.ExportEvents = true;
	options.Features.ViewCapabilities = true;
	options.Features.ViewPoisonedOutbox = true;
	options.Features.ViewManifest = true;
	options.Features.ViewUnknownEvents = true;
	options.Features.ViewSnapshot = true;
	options.Features.RebuildSnapshot = true;
});
builder.AddPurviewEventSourcingAdminOpenAPI();
builder.Services.AddPurviewEventSourcingAdminSecurity();
builder.Services.AddEventSourcing();

// The query services are only required so minimal-API metadata inference succeeds while building the document;
// they are never invoked during export.
builder.Services.AddSingleton<IAdminAggregateQueryService, StubAggregateQueryService>();
builder.Services.AddSingleton<IAdminEventQueryService, StubEventQueryService>();
builder.Services.AddSingleton<IAdminProjectionService, StubProjectionService>();

var app = builder.Build();

app.MapPurviewEventSourcingAdminAPI();
app.MapOpenApi();

app.Urls.Clear();
app.Urls.Add("http://127.0.0.1:0");

await app.StartAsync();

var serverAddresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
var baseAddress =
	serverAddresses?.Addresses.FirstOrDefault()
	?? throw new InvalidOperationException("Could not determine the Kestrel bound address.");

using HttpClient client = new();
Uri documentUrl = new($"{baseAddress}/openapi/{builder.OpenAPIDocumentName}.json");
var json = await client.GetStringAsync(documentUrl);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, json);

await app.StopAsync();

Console.WriteLine($"Admin API OpenAPI document written to {Path.GetFullPath(outputPath)}.");

static string ResolveOutputPath(string[] args)
{
	if (args.Length > 0 && Path.IsPathRooted(args[0]))
		return args[0];

	// WebApplication.CreateBuilder changes the current directory to the content root, so relative paths are
	// resolved against the repository root (found by walking up to the nearest nuget.config) instead.
	var root = FindRepositoryRoot(Path.GetFullPath("."));
	var relative = args.Length > 0 ? args[0] : "./src/src/Admin.Client/OpenAPI/admin.openapi.json";
	return Path.GetFullPath(Path.Combine(root, relative));
}

static string FindRepositoryRoot(string start)
{
	var current = start;
	while (current is not null)
	{
		if (File.Exists(Path.Combine(current, "nuget.config")))
			return current;

		current = Path.GetDirectoryName(current);
	}

	return start;
}

sealed class StubAggregateQueryService : IAdminAggregateQueryService
{
	public Task<PagedResult<AggregateSummaryResponse>> SearchAsync(
		AggregateSearchQuery query,
		CancellationToken cancellationToken
	) => throw new NotSupportedException();

	public Task<Result<AggregateSummaryResponse, AdminQueryFailure>> GetAsync(
		string aggregateType,
		string aggregateId,
		CancellationToken cancellationToken
	) => throw new NotSupportedException();
}

sealed class StubEventQueryService : IAdminEventQueryService
{
	public Task<Result<PagedResult<EventEnvelopeResponse>, AdminQueryFailure>> GetRangeAsync(
		string aggregateType,
		string aggregateId,
		EventRangeQuery query,
		CancellationToken cancellationToken
	) => throw new NotSupportedException();
}

sealed class StubProjectionService : IAdminProjectionService
{
	public Task<Result<ProjectionResponse, AdminQueryFailure>> ProjectAtVersionAsync(
		string aggregateType,
		string aggregateId,
		long targetVersion,
		CancellationToken cancellationToken
	) => throw new NotSupportedException();

	public Task<Result<ProjectionResponse, AdminQueryFailure>> ProjectAtTimeAsync(
		string aggregateType,
		string aggregateId,
		DateTimeOffset targetUtc,
		CancellationToken cancellationToken
	) => throw new NotSupportedException();
}
