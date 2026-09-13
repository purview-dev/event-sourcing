// No authentication in this sample — allow all operations without a principal identifier
using Purview.EventSourcing.Samples.Web.Services;

EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = false;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults().AddSampleEventStore().AddSampleServices();

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddSession(options =>
{
	options.IdleTimeout = TimeSpan.FromMinutes(30);
	options.Cookie.HttpOnly = true;
	options.Cookie.IsEssential = true;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
	app.UseExceptionHandler("/Error");

app.UseStaticFiles().UseRouting().UseAuthentication().UseAuthorization().UseSession();

app.UseAdminAPI();

app.MapDefaultEndpoints().MapAudit().MapGet("/pingz", () => Results.Ok());

await using (var scope = app.Services.CreateAsyncScope())
{
	var seedService = scope.ServiceProvider.GetRequiredService<SampleSeedService>();
	await seedService.SeedAsync();
}

await app.RunAsync();
