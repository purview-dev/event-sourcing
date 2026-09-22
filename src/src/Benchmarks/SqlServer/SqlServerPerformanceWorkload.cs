namespace Purview.EventSourcing.Benchmarks.SqlServer;

sealed class SqlServerPerformanceWorkload
{
	public int AggregateCount { get; set; }

	public int EventsPerAggregate { get; set; }

	public int QueryIterations { get; set; }
}
