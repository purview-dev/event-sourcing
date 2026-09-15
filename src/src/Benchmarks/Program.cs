using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Purview.EventSourcing.Benchmarks;

// The suites run with the in-process toolchain: benchmarks execute directly in this process, which
// avoids BenchmarkDotNet generating an out-of-process boilerplate project. The repository's
// Purview.DotNetProjectSdk cannot build under BenchmarkDotNet's generated project layout, and an
// in-process run keeps the harness self-contained and fast to invoke.

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
var runBenchmark = Array.Exists(
	args,
	static arg => string.Equals(arg, "--benchmark", StringComparison.OrdinalIgnoreCase)
);

switch (mode)
{
	case "source-generator":
	case "sg":
		return await RunSuiteAsync(
			"source-generator",
			"SourceGeneratorPerformance",
			typeof(SourceGeneratorPerformanceBenchmarks),
			BenchmarkPolicies.SourceGenerator,
			runBenchmark
		);
	case "runtime":
	case "rt":
		return await RunSuiteAsync(
			"runtime",
			"RuntimePerformance",
			typeof(RuntimePerformanceBenchmarks),
			BenchmarkPolicies.Runtime,
			runBenchmark
		);
	case "sql-server":
	case "sql":
		return await RunSuiteAsync(
			"sql-server",
			"SqlServerPerformance",
			typeof(SqlServerPerformanceBenchmarks),
			BenchmarkPolicies.SqlServer,
			runBenchmark
		);
	case "inmemory":
	case "im":
		return await RunSuiteAsync(
			"inmemory",
			"InMemoryPerformance",
			typeof(InMemoryPerformanceBenchmarks),
			BenchmarkPolicies.InMemory,
			runBenchmark
		);
	case "all":
	{
		var sourceGeneratorExit = await RunSuiteAsync(
			"source-generator",
			"SourceGeneratorPerformance",
			typeof(SourceGeneratorPerformanceBenchmarks),
			BenchmarkPolicies.SourceGenerator,
			runBenchmark
		);
		var runtimeExit = await RunSuiteAsync(
			"runtime",
			"RuntimePerformance",
			typeof(RuntimePerformanceBenchmarks),
			BenchmarkPolicies.Runtime,
			runBenchmark
		);
		var sqlServerExit = await RunSuiteAsync(
			"sql-server",
			"SqlServerPerformance",
			typeof(SqlServerPerformanceBenchmarks),
			BenchmarkPolicies.SqlServer,
			runBenchmark
		);
		var inMemoryExit = await RunSuiteAsync(
			"inmemory",
			"InMemoryPerformance",
			typeof(InMemoryPerformanceBenchmarks),
			BenchmarkPolicies.InMemory,
			runBenchmark
		);
		return Math.Max(Math.Max(Math.Max(sourceGeneratorExit, runtimeExit), sqlServerExit), inMemoryExit);
	}
	default:
		await Console.Error.WriteLineAsync(
			$"Unknown benchmark '{mode}'. Expected 'source-generator', 'runtime', 'inmemory', 'sql-server', or 'all'."
		);
		return 2;
}

static async Task<int> RunSuiteAsync(
	string mode,
	string suiteName,
	Type benchmarkType,
	BenchmarkSuitePolicy policy,
	bool runBenchmark
)
{
	var config = ManualConfig
		.Create(DefaultConfig.Instance)
		.WithArtifactsPath(Path.Combine("artifacts", "benchmarkdotnet", mode))
		.AddJob(CreateJob(runBenchmark));

	var summary = BenchmarkRunner.Run(benchmarkType, config);

	if (summary.HasCriticalValidationErrors)
	{
		await Console.Error.WriteLineAsync(
			$"Benchmark validation failed for the {suiteName} suite. Run in Release configuration (e.g. just perf-{mode})."
		);
		return 1;
	}

	var run = await BenchmarkRunExporter.ExportAsync(mode, suiteName, summary, policy, CancellationToken.None);

	await Console.Out.WriteLineAsync();
	foreach (var line in run.FormatSummary())
		await Console.Out.WriteLineAsync(line);

	return run.Passed ? 0 : 1;
}

static Job CreateJob(bool runBenchmark)
{
	var job = runBenchmark
		? new Job
		{
			Run = { WarmupCount = 3, IterationCount = 12 },
			Accuracy = { MinIterationTime = Perfolizer.Horology.TimeInterval.FromMilliseconds(100) },
		}
		: new Job
		{
			Run = { WarmupCount = 1, IterationCount = 3 },
			Accuracy = { MinIterationTime = Perfolizer.Horology.TimeInterval.FromMilliseconds(50) },
		};

	return job.WithToolchain(InProcessEmitToolchain.Instance);
}
