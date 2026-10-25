using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing;

/// <summary>
/// Helpers that keep the metric tags identical across storage providers.
/// </summary>
public static class EventStoreMetricsExtensions
{
	/// <summary>
	/// Records the duration and outcome of a save.
	/// </summary>
	/// <typeparam name="T">The aggregate type.</typeparam>
	/// <param name="metrics">The metrics recorder.</param>
	/// <param name="milliseconds">The elapsed time, in milliseconds.</param>
	/// <param name="aggregateType">The aggregate type name.</param>
	/// <param name="result">The save result.</param>
	/// <remarks>
	/// Only a returned result is recorded. An exception escaping a save is an infrastructure fault
	/// rather than an outcome, and is already reported through logging and the activity.
	/// </remarks>
	public static void RecordSave<T>(
		this IEventStoreMetrics metrics,
		double milliseconds,
		string aggregateType,
		Result<SaveOutcome<T>, AggregateSaveFailure> result
	)
		where T : class, IAggregate, new()
	{
		ArgumentNullException.ThrowIfNull(metrics);

		metrics.SaveDuration(milliseconds, aggregateType, OutcomeTag(result));
	}

	static string OutcomeTag<T>(Result<SaveOutcome<T>, AggregateSaveFailure> result)
		where T : class, IAggregate, new()
	{
		if (result.IsFailure)
		{
			return result.Error switch
			{
				ConcurrencyConflict => "concurrency-conflict",
				AggregateLocked => "locked",
				AggregateDeleted => "deleted",
				AggregateInvalid => "invalid",
				_ => "failed",
			};
		}

		return result.Value.Kind switch
		{
			SaveOutcomeKind.Written => "written",
			SaveOutcomeKind.NoChanges => "no-changes",
			SaveOutcomeKind.AlreadyApplied => "already-applied",
			_ => "unknown",
		};
	}
}
