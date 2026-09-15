using System.Runtime.InteropServices;
using BenchmarkDotNet.Reports;

namespace Purview.EventSourcing.Benchmarks;

static class BenchmarkRunExporter
{
	public static async Task<BenchmarkSuiteRun> ExportAsync(
		string mode,
		string suiteName,
		Summary summary,
		BenchmarkSuitePolicy policy,
		CancellationToken cancellationToken
	)
	{
		var run = new BenchmarkSuiteRun
		{
			Mode = mode,
			SuiteName = suiteName,
			TimestampUtc = DateTimeOffset.UtcNow,
			MachineName = Environment.MachineName,
			FrameworkDescription = RuntimeInformation.FrameworkDescription,
		};

		foreach (var report in summary.Reports)
		{
			if (report.ResultStatistics is null || report.ResultStatistics.Mean <= 0)
				continue;

			run.Cases.Add(BuildCase(report));
		}

		ApplyCasePolicies(run, policy);
		ApplyRatioPolicies(run, policy);
		ApplyPreviousComparison(run, policy);

		var store = new BenchmarkHistoryStore(mode);
		var savedPath = await store.SaveAsync(run, cancellationToken);
		run.Warnings.Add($"Saved results to {savedPath}");

		return run;
	}

	static BenchmarkCaseRecord BuildCase(BenchmarkReport report)
	{
		var record = new BenchmarkCaseRecord
		{
			Name = report.BenchmarkCase.DisplayInfo,
			Benchmark = report.BenchmarkCase.Descriptor.WorkloadMethod.Name,
			Scenario = string.Join(
				", ",
				report.BenchmarkCase.Parameters.Items.Select(static parameter =>
					parameter.Value?.ToString() ?? string.Empty
				)
			),
		};

		if (report.ResultStatistics is not null)
		{
			record.MeanMilliseconds = report.ResultStatistics.Mean / 1_000_000d;
			record.StdDevMilliseconds = report.ResultStatistics.StandardDeviation / 1_000_000d;
			record.OperationsPerSecond =
				report.ResultStatistics.Mean > 0 ? 1_000_000_000d / report.ResultStatistics.Mean : 0;
		}

		if (report.Metrics.TryGetValue("Allocated Memory", out var allocated))
			record.AllocatedBytes = (long)Math.Round(allocated.Value);

		return record;
	}

	static void ApplyCasePolicies(BenchmarkSuiteRun run, BenchmarkSuitePolicy policy)
	{
		foreach (var caseRecord in run.Cases)
		{
			var casePolicy = policy.CasePolicies.FirstOrDefault(casePolicy =>
				caseRecord.Name.Contains(casePolicy.NamePattern, StringComparison.Ordinal)
			);

			if (casePolicy is null)
				continue;

			if (casePolicy.MaxMeanMilliseconds is double maxMean && caseRecord.MeanMilliseconds > maxMean)
			{
				caseRecord.MaxAllowedMilliseconds = maxMean;
				caseRecord.Passed = false;
				run.Failures.Add(
					$"[{caseRecord.Name}] mean {caseRecord.MeanMilliseconds:F2}ms exceeds the {maxMean:F2}ms threshold."
				);
			}
		}
	}

	static void ApplyRatioPolicies(BenchmarkSuiteRun run, BenchmarkSuitePolicy policy)
	{
		foreach (var ratioPolicy in policy.RatioPolicies)
		{
			var denominator = FindCase(run, ratioPolicy.DenominatorBenchmark, ratioPolicy.ScenarioPattern);
			var numerator = FindCase(run, ratioPolicy.NumeratorBenchmark, ratioPolicy.ScenarioPattern);

			if (denominator is null || numerator is null)
			{
				run.Warnings.Add(
					$"Ratio check '{ratioPolicy.NumeratorBenchmark}/{ratioPolicy.DenominatorBenchmark}' for '{ratioPolicy.ScenarioPattern}' could not be evaluated (missing case)."
				);
				continue;
			}

			if (denominator.MeanMilliseconds <= 0)
				continue;

			var ratio = numerator.MeanMilliseconds / denominator.MeanMilliseconds;
			if (ratio > ratioPolicy.MaxRatio)
			{
				run.Failures.Add(
					$"[{ratioPolicy.ScenarioPattern}] {ratioPolicy.NumeratorBenchmark} took {ratio:P0} of {ratioPolicy.DenominatorBenchmark}, exceeding the {ratioPolicy.MaxRatio:P0} threshold."
				);
			}
		}
	}

	static void ApplyPreviousComparison(BenchmarkSuiteRun run, BenchmarkSuitePolicy policy)
	{
		var previousRun = new BenchmarkHistoryStore(run.Mode).TryLoadLatest();
		if (previousRun is null)
		{
			run.Warnings.Add("No previous run recorded; skipped regression comparison.");
			return;
		}

		var previousByName = previousRun
			.Cases.GroupBy(static record => record.Name, StringComparer.Ordinal)
			.ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

		foreach (var caseRecord in run.Cases)
		{
			if (!previousByName.TryGetValue(caseRecord.Name, out var previous))
				continue;

			if (policy.DefaultMaxMeanRegressionPercent is double maxMeanPercent)
			{
				if (previous.MeanMilliseconds > 0)
				{
					var regression =
						(caseRecord.MeanMilliseconds - previous.MeanMilliseconds) / previous.MeanMilliseconds;
					if (regression > maxMeanPercent / 100d)
					{
						run.Failures.Add(
							$"[{caseRecord.Name}] mean regressed by {regression:P0} vs previous run ({previous.MeanMilliseconds:F2}ms -> {caseRecord.MeanMilliseconds:F2}ms), exceeding the {maxMeanPercent:P0} threshold."
						);
					}
				}
			}

			if (
				policy.DefaultMaxAllocationRegressionPercent is double maxAllocationPercent
				&& previous.AllocatedBytes is long previousAllocation
				&& caseRecord.AllocatedBytes is long currentAllocation
			)
			{
				if (previousAllocation > 0 && currentAllocation > previousAllocation)
				{
					var regression = (double)(currentAllocation - previousAllocation) / previousAllocation;
					if (regression > maxAllocationPercent / 100d)
					{
						run.Failures.Add(
							$"[{caseRecord.Name}] allocations regressed by {regression:P0} vs previous run ({previousAllocation:N0}B -> {currentAllocation:N0}B), exceeding the {maxAllocationPercent:P0} threshold."
						);
					}
				}
			}
		}
	}

	static BenchmarkCaseRecord? FindCase(BenchmarkSuiteRun run, string benchmark, string scenarioPattern)
	{
		foreach (var caseRecord in run.Cases)
		{
			if (
				caseRecord.Benchmark == benchmark
				&& caseRecord.Scenario.Contains(scenarioPattern, StringComparison.Ordinal)
			)
				return caseRecord;
		}

		return null;
	}
}
