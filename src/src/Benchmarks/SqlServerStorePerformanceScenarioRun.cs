namespace Purview.EventSourcing.SqlServer;

sealed class SqlServerStorePerformanceScenarioRun
{
	public string Name { get; set; } = string.Empty;

	public int OperationCount { get; set; }

	public double TotalMilliseconds { get; set; }

	public double AverageMilliseconds { get; set; }

	public double OperationsPerSecond { get; set; }

	public double MaxAllowedAverageMilliseconds { get; set; }

	public bool Passed { get; set; }

	public string FormatCurrent()
	{
		var status = Passed ? "PASS" : "FAIL";
		return $"{Name} {status} total={TotalMilliseconds:F2}ms ops={OperationCount} avg={AverageMilliseconds:F2}ms ops/s={OperationsPerSecond:F2} threshold(avg)<={MaxAllowedAverageMilliseconds:F2}ms";
	}

	public string FormatComparison(SqlServerStorePerformanceScenarioRun previous) =>
		$"  vs previous: avg={FormatDelta(AverageMilliseconds - previous.AverageMilliseconds, previous.AverageMilliseconds)} ops/s={FormatDelta(OperationsPerSecond - previous.OperationsPerSecond, previous.OperationsPerSecond)}";

	static string FormatDelta(double delta, double previous)
	{
		var percent = previous <= 0 ? 0 : delta / previous * 100;
		return $"{delta:+0.00;-0.00;0.00} ({percent:+0.0;-0.0;0.0}%)";
	}
}
