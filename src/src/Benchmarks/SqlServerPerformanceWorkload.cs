namespace Purview.EventSourcing.SqlServer;

sealed class SqlServerPerformanceWorkload
{
	public int AggregateCount { get; set; }

	public int EventsPerAggregate { get; set; }

	public int QueryIterations { get; set; }
}
