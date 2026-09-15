namespace Purview.EventSourcing.Benchmarks;

static class BenchmarkPolicies
{
	/// <summary>
	/// The source-generator suite is guarded by structural ratios plus a previous-run comparison.
	/// The pre-compilation marker makes identical reruns short-circuit: warm reruns measure ~6-12% of
	/// cold generation. The warm-rerun threshold guards against the marker silently regressing (a
	/// rerun that regenerates everything again), while the single-edit threshold allows the inherent
	/// transform re-execution when one aggregate changes.
	/// </summary>
	public static BenchmarkSuitePolicy SourceGenerator { get; } =
		new()
		{
			DefaultMaxMeanRegressionPercent = 40,
			RatioPolicies =
			[
				new BenchmarkRatioPolicy("AggregateSimple", "WarmRerun", "ColdGeneration", 0.40),
				new BenchmarkRatioPolicy("AggregateWithValueObjects", "WarmRerun", "ColdGeneration", 0.40),
				new BenchmarkRatioPolicy("AggregateMulti", "WarmRerun", "ColdGeneration", 0.40),
				new BenchmarkRatioPolicy("ScalarValueObject", "WarmRerun", "ColdGeneration", 0.40),
				new BenchmarkRatioPolicy("ComplexValueObject", "WarmRerun", "ColdGeneration", 0.40),
				new BenchmarkRatioPolicy("AggregateMulti", "SingleAggregateEdit", "ColdGeneration", 1.50),
			],
		};

	/// <summary>
	/// The runtime suite measures generated-code hot paths. Allocations are the most deterministic
	/// signal (an extra event allocation or delegate per operation is immediately visible), so they
	/// are guarded with a tighter regression threshold than wall time.
	/// </summary>
	public static BenchmarkSuitePolicy Runtime { get; } =
		new() { DefaultMaxMeanRegressionPercent = 40, DefaultMaxAllocationRegressionPercent = 10 };

	/// <summary>
	/// Absolute per-operation thresholds for the SQL Server suite, matching the historical harness.
	/// </summary>
	public static BenchmarkSuitePolicy SqlServer { get; } =
		new()
		{
			CasePolicies =
			[
				new BenchmarkCasePolicy("EventStore_Save", MaxMeanMilliseconds: 90),
				new BenchmarkCasePolicy("EventStore_Save_NoSnapshot", MaxMeanMilliseconds: 70),
				new BenchmarkCasePolicy("EventStore_Get", MaxMeanMilliseconds: 30),
				new BenchmarkCasePolicy("SnapshotStore_Snapshot", MaxMeanMilliseconds: 35),
				new BenchmarkCasePolicy("SnapshotStore_Query", MaxMeanMilliseconds: 40),
				new BenchmarkCasePolicy("SnapshotStore_Query_ComplexAggregate", MaxMeanMilliseconds: 65),
			],
		};

	/// <summary>
	/// The in-memory suite is the allocation-free reference; it is guarded by the same regression
	/// thresholds as the runtime suite.
	/// </summary>
	public static BenchmarkSuitePolicy InMemory { get; } =
		new() { DefaultMaxMeanRegressionPercent = 40, DefaultMaxAllocationRegressionPercent = 10 };
}
