using Purview.EventSourcing.SourceGenerator;
using Purview.EventSourcing.SqlServer;

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
var runBenchmark = Array.Exists(
	args,
	static arg => string.Equals(arg, "--benchmark", StringComparison.OrdinalIgnoreCase)
);

switch (mode)
{
	case "source-generator":
	case "sg":
		return await RunSourceGeneratorAsync(runBenchmark);
	case "sql-server":
	case "sql":
		return await RunSqlServerAsync(runBenchmark);
	case "all":
	{
		var sourceGeneratorExit = await RunSourceGeneratorAsync(runBenchmark);
		var sqlServerExit = await RunSqlServerAsync(runBenchmark);
		return Math.Max(sourceGeneratorExit, sqlServerExit);
	}
	default:
		await Console.Error.WriteLineAsync(
			$"Unknown benchmark '{mode}'. Expected 'source-generator', 'sql-server', or 'all'."
		);
		return 2;
}

static Task<int> RunSourceGeneratorAsync(bool runBenchmark)
{
	SourceGeneratorPerformanceRunner runner = new();
	PerformanceHistoryStore store = new();

	var previousRun = store.TryLoadLatest();
	var run = runBenchmark ? runner.RunBenchmark() : runner.RunQuick();
	var savedPath = store.Save(run);

	Console.WriteLine($"Saved {run.Mode} results to {savedPath}");
	Console.WriteLine();

	foreach (var line in run.FormatSummary(previousRun))
		Console.WriteLine(line);

	return Task.FromResult(0);
}

static async Task<int> RunSqlServerAsync(bool runBenchmark)
{
	SqlServerStorePerformanceRunner runner = new();
	SqlServerStorePerformanceHistoryStore store = new();

	using CancellationTokenSource cancellationTokenSource = new();
	Console.CancelKeyPress += (s, e) => cancellationTokenSource.Cancel();

	var previousRun = store.TryLoadLatest();
	var run = await (
		runBenchmark
			? runner.RunBenchmarkAsync(cancellationTokenSource.Token)
			: runner.RunQuickAsync(cancellationTokenSource.Token)
	);
	var savedPath = await store.SaveAsync(run, cancellationTokenSource.Token);

	await Console.Out.WriteLineAsync($"Saved {run.Mode} results to {savedPath}");
	await Console.Out.WriteLineAsync();

	foreach (var line in run.FormatSummary(previousRun))
		await Console.Out.WriteLineAsync(line);

	if (run.Passed)
		return 0;

	await Console.Out.WriteLineAsync();
	await Console.Error.WriteLineAsync("Performance thresholds were not met.");

	return 1;
}
