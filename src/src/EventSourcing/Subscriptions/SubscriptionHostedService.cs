using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// Drives a subscription's catch-up cycles on a fixed interval.
/// </summary>
/// <remarks>
/// A scope is created per cycle so scoped services - the event store, the current tenant - resolve
/// the way they do for a request.
/// </remarks>
/// <typeparam name="T">The aggregate type the subscription reads.</typeparam>
public sealed class SubscriptionHostedService<T>(
	IServiceScopeFactory scopeFactory,
	IOptions<SubscriptionOptions> options,
	ILogger<SubscriptionHostedService<T>> logger
) : BackgroundService
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				using var scope = scopeFactory.CreateScope();
				var processor = scope.ServiceProvider.GetRequiredService<IEventSubscriptionProcessor<T>>();
				await processor.RunCycleAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			// A store or discovery failure must not terminate the loop; the next cycle retries.
#pragma warning disable CA1031
			catch (Exception ex)
#pragma warning restore CA1031
			{
				logger.LogError(
					ex,
					"Subscription cycle for aggregate type {AggregateType} failed; the loop will retry after the poll interval.",
					typeof(T).Name
				);
			}

			await Task.Delay(options.Value.PollInterval, stoppingToken);
		}
	}
}
