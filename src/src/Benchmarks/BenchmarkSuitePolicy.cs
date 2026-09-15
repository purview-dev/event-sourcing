namespace Purview.EventSourcing.Benchmarks;

/// <summary>
/// Threshold rule for a benchmark case, matched by substring against the full BenchmarkDotNet
/// case display name.
/// </summary>
sealed record BenchmarkCasePolicy(
	string NamePattern,
	double? MaxMeanMilliseconds = null,
	double? MaxMeanRegressionPercent = null,
	double? MaxAllocationRegressionPercent = null
);

/// <summary>
/// Structural rule comparing two benchmark methods for the same scenario. Used by the
/// source-generator suite to guard the warm-rerun / single-edit incremental-cache ratios.
/// </summary>
sealed record BenchmarkRatioPolicy(
	string ScenarioPattern,
	string NumeratorBenchmark,
	string DenominatorBenchmark,
	double MaxRatio
);

sealed class BenchmarkSuitePolicy
{
	public IReadOnlyList<BenchmarkCasePolicy> CasePolicies { get; init; } = [];

	public IReadOnlyList<BenchmarkRatioPolicy> RatioPolicies { get; init; } = [];

	public double? DefaultMaxMeanRegressionPercent { get; init; }

	public double? DefaultMaxAllocationRegressionPercent { get; init; }
}
