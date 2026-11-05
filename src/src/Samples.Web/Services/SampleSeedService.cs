using System.Diagnostics;
using Azure;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Npgsql;
// Commit and concurrency failures are one type across every provider now, so the sample catches each
// once rather than once per store.
using Purview.EventSourcing.Exceptions;
using Purview.EventSourcing.Samples.Options;
using Purview.EventSourcing.Samples.Services;

namespace Purview.EventSourcing.Samples.Web.Services;

sealed class SampleSeedService(
	ISeedDataService seedDataService,
	IOptions<SampleStoreOptions> sampleStoreOptions,
	ISampleSeedServiceTelemetry telemetry
)
{
	public async Task SeedAsync(CancellationToken cancellationToken = default)
	{
		for (var attempt = 0; ; attempt++)
		{
			telemetry.Seeding(sampleStoreOptions.Value.CurrentKey, attempt + 1);

			try
			{
				var sw = Stopwatch.StartNew();
				var seeded = await seedDataService.SeedAsync(cancellationToken);

				sw.Stop();

				if (!seeded && attempt < 2)
				{
					// Another app instance may be seeding the demo store at the same time.
					await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
					continue;
				}

				telemetry.Complete(sampleStoreOptions.Value.CurrentKey, attempt + 1, sw.Elapsed);

				return;
			}
			catch (RequestFailedException ex)
			{
				telemetry.RequestFailed(sampleStoreOptions.Value.CurrentKey, ex);
				break;
			}
			catch (MongoException ex)
			{
				telemetry.MongoFailed(sampleStoreOptions.Value.CurrentKey, ex);
				break;
			}
			catch (NpgsqlException ex)
			{
				telemetry.NpgsqlFailed(sampleStoreOptions.Value.CurrentKey, ex);
				break;
			}
			catch (CommitException ex)
			{
				telemetry.CommitFailed(sampleStoreOptions.Value.CurrentKey, ex);
				break;
			}
			catch (FormatException ex)
			{
				telemetry.FormatFailed(sampleStoreOptions.Value.CurrentKey, ex);
				break;
			}
			catch (ArgumentException ex)
			{
				telemetry.ArgumentFailed(sampleStoreOptions.Value.CurrentKey, ex);
				break;
			}
			catch (InvalidOperationException ex)
			{
				telemetry.InvalidOperationFailed(sampleStoreOptions.Value.CurrentKey, ex);
				break;
			}
		}
	}
}
