namespace Purview.EventSourcing.Benchmarks;

static class BenchmarkPolicies
{
	/// <summary>
	/// The source-generator suite is guarded by structural ratios plus a previous-run comparison.
	/// The incremental pipeline currently re-executes most aggregate-generation steps on an identical
	/// rerun (a warm rerun measures close to cold generation), so the ratio thresholds are regression
	/// guards that catch gross pipeline breaks (for example regenerating everything *and* making it
	/// slower) rather than aspirational targets. See <c>docs/wiki/Source-Generator-Performance.md</c>
	/// for the known incremental-caching hotspot.
	/// </summary>
	public static BenchmarkSuitePolicy SourceGenerator { get; } =
		new()
		{
			DefaultMaxMeanRegressionPercent = 40,
			RatioPolicies =
			[
				new BenchmarkRatioPolicy("AggregateSimple", "WarmRerun", "ColdGeneration", 1.50),
				new BenchmarkRatioPolicy("AggregateWithValueObjects", "WarmRerun", "ColdGeneration", 1.50),
				new BenchmarkRatioPolicy("AggregateMulti", "WarmRerun", "ColdGeneration", 1.50),
				new BenchmarkRatioPolicy("ScalarValueObject", "WarmRerun", "ColdGeneration", 1.50),
				new BenchmarkRatioPolicy("ComplexValueObject", "WarmRerun", "ColdGeneration", 1.50),
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
