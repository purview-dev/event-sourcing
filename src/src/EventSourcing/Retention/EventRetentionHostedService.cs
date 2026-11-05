using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Retention;

/// <summary>
/// Runs event-stream retention on an interval, when the scheduled mode is enabled.
/// </summary>
/// <remarks>
/// <para>
/// Runs under <see cref="Tenancy.TenantScope.System"/>, because a schedule has no signed-in user to
/// take a tenant from and the alternative - guessing - would prune whichever tenant happened to be
/// ambient.
/// </para>
/// <para>
/// A failure does not stop the loop; the next interval retries. Retention falling behind is a
/// storage problem, whereas a crashed host is an availability one.
/// </para>
/// </remarks>
/// <typeparam name="T">The aggregate type to prune.</typeparam>
public sealed partial class EventRetentionHostedService<T>(
	IServiceScopeFactory scopeFactory,
	IOptions<EventRetentionOptions> options,
	ILogger<EventRetentionHostedService<T>> logger
) : BackgroundService
	where T : class, IAggregate, new()
{
	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (!options.Value.Mode.HasFlag(EventRetentionMode.Scheduled))
			return;

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				using var scope = scopeFactory.CreateScope();
				var retention = scope.ServiceProvider.GetRequiredService<IEventStreamRetention<T>>();

				using (Tenancy.AmbientCurrentTenant.BeginScope(Tenancy.TenantScope.System))
					await retention.PruneAsync(options.Value.Policy, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			// A retention failure must not terminate the loop; the next interval retries.
#pragma warning disable CA1031
			catch (Exception ex)
#pragma warning restore CA1031
			{
				LogRetentionCycleFailed(logger, typeof(T).Name, ex);
			}

			await Task.Delay(options.Value.Interval, stoppingToken);
		}
	}

	[LoggerMessage(
		EventId = 1,
		Level = LogLevel.Error,
		Message = "Scheduled event retention for {AggregateType} failed; the loop will retry after the interval."
	)]
	static partial void LogRetentionCycleFailed(ILogger logger, string aggregateType, Exception exception);
}
