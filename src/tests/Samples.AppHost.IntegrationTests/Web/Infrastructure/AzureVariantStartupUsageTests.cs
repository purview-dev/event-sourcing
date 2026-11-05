using Purview.EventSourcing.Samples.Fixtures;
using Purview.EventSourcing.Samples.Options;

namespace Purview.EventSourcing.Samples.Web.Infrastructure;

[NotInParallel("SamplesAppHost")]
[ClassDataSource<AppHostFixture>(Shared = SharedType.PerTestSession)]
public sealed class AzureVariantStartupUsageTests(AppHostFixture fixture)
{
	static readonly string[] ForbiddenStartupLogMarkers =
	[
		"Sample data seeding failed",
		"System.FormatException: No valid combination of account information found.",
		"Unhandled exception",
	];

	[Test]
	public Task AzureSqlVariant_StartsAndServesDashboard(CancellationToken cancellationToken) =>
		AssertVariantDashboardAsync(Platform.AzureSqlWebApp, SampleQueryStoreKind.SqlServer, cancellationToken);

	[Test]
	public Task AzurePostgresVariant_StartsAndServesDashboard(CancellationToken cancellationToken) =>
		AssertVariantDashboardAsync(Platform.AzurePostgresWebApp, SampleQueryStoreKind.Postgres, cancellationToken);

	[Test]
	public Task AzureMongoVariant_StartsAndServesDashboard(CancellationToken cancellationToken) =>
		AssertVariantDashboardAsync(Platform.AzureMongoDBWebApp, SampleQueryStoreKind.MongoDB, cancellationToken);

	async Task AssertVariantDashboardAsync(
		string resourceName,
		SampleQueryStoreKind expectedQueryStore,
		CancellationToken cancellationToken
	)
	{
		using var client = fixture.CreateWebClient(resourceName, followRedirects: true);

		var html = await GetDashboardHtmlAsync(client, cancellationToken);

		await Assert.That(html).Contains("Customer Experience");
		await Assert.That(html).Contains("Back Office");

		// The dashboard must report the requested variant's backing stores rather than a shared default.
		await Assert.That(html).Contains("Event: AzureStorage");
		await Assert.That(html).Contains($"Query: {expectedQueryStore}");

		var logs = await fixture.GetResourceLogsAsync(resourceName, maxLines: 500);
		await Assert
			.That(
				logs.Any(line =>
					ForbiddenStartupLogMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase))
				)
			)
			.IsFalse();
	}

	static async Task<string> GetDashboardHtmlAsync(HttpClient client, CancellationToken cancellationToken)
	{
		// The Aspire fixture reports the project as running before Kestrel accepts traffic; retry
		// briefly until the dashboard responds successfully.
		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

		while (true)
		{
			try
			{
				var response = await client.GetAsync("/", timeoutCts.Token);
				if (response.IsSuccessStatusCode)
					return await response.Content.ReadAsStringAsync(timeoutCts.Token);
			}
			catch (HttpRequestException) when (!timeoutCts.IsCancellationRequested)
			{
				// Connection refused: Kestrel has not bound the port yet. This is the case the retry
				// exists for, and it arrives as an exception rather than a status code.
			}

			await Task.Delay(TimeSpan.FromMilliseconds(500), timeoutCts.Token);
		}
	}
}
