namespace Purview.EventSourcing.SqlServer;

sealed class SqlServerStorePerformanceRun
{
	public string Mode { get; set; } = string.Empty;

	public DateTimeOffset TimestampUtc { get; set; }

	public string MachineName { get; set; } = string.Empty;

	public string FrameworkDescription { get; set; } = string.Empty;

	public SqlServerPerformanceWorkload Workload { get; set; } = new();

	public List<SqlServerStorePerformanceScenarioRun> Scenarios { get; set; } = [];

	public bool Passed => Scenarios.All(static scenario => scenario.Passed);

	public IEnumerable<string> FormatSummary(SqlServerStorePerformanceRun? previousRun)
	{
		yield return $"Mode: {Mode}";
		yield return $"Timestamp (UTC): {TimestampUtc:O}";
		yield return $"Framework: {FrameworkDescription}";
		yield return $"Machine: {MachineName}";
		yield return $"Workload: aggregates={Workload.AggregateCount}, eventsPerAggregate={Workload.EventsPerAggregate}, queryIterations={Workload.QueryIterations}";
		yield return string.Empty;

		var previousScenarios =
			previousRun?.Scenarios.ToDictionary(static scenario => scenario.Name, StringComparer.Ordinal)
			?? [with(StringComparer.Ordinal)];

		foreach (var scenario in Scenarios)
		{
			yield return scenario.FormatCurrent();

			if (previousScenarios.TryGetValue(scenario.Name, out var previousScenario))
				yield return scenario.FormatComparison(previousScenario);
		}

		yield return string.Empty;
		yield return Passed ? "Result: PASS" : "Result: FAIL";
	}
}
