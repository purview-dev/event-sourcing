namespace Purview.EventSourcing.Benchmarks;

sealed class BenchmarkSuiteRun
{
	public string Mode { get; set; } = string.Empty;

	public string SuiteName { get; set; } = string.Empty;

	public DateTimeOffset TimestampUtc { get; set; }

	public string MachineName { get; set; } = string.Empty;

	public string FrameworkDescription { get; set; } = string.Empty;

	public List<BenchmarkCaseRecord> Cases { get; set; } = [];

	public List<string> Failures { get; set; } = [];

	public List<string> Warnings { get; set; } = [];

	public bool Passed => Failures.Count == 0;

	public IEnumerable<string> FormatSummary()
	{
		yield return $"Suite: {SuiteName}";
		yield return $"Mode: {Mode}";
		yield return $"Timestamp (UTC): {TimestampUtc:O}";
		yield return $"Framework: {FrameworkDescription}";
		yield return $"Machine: {MachineName}";
		yield return string.Empty;

		foreach (var caseRecord in Cases)
			yield return caseRecord.Format();

		if (Failures.Count > 0)
		{
			yield return string.Empty;
			foreach (var failure in Failures)
				yield return $"  FAILURE: {failure}";
		}

		if (Warnings.Count > 0)
		{
			yield return string.Empty;
			foreach (var warning in Warnings)
				yield return $"  WARNING: {warning}";
		}

		yield return string.Empty;
		yield return Passed ? "Result: PASS" : "Result: FAIL";
	}
}

sealed class BenchmarkCaseRecord
{
	public string Name { get; set; } = string.Empty;

	public string Benchmark { get; set; } = string.Empty;

	public string Scenario { get; set; } = string.Empty;

	public double MeanMilliseconds { get; set; }

	public double? StdDevMilliseconds { get; set; }

	public double OperationsPerSecond { get; set; }

	public long? AllocatedBytes { get; set; }

	public double? MaxAllowedMilliseconds { get; set; }

	public bool Passed { get; set; } = true;

	public string? Note { get; set; }

	public string Format()
	{
		var status = Passed ? "PASS" : "FAIL";
		var allocation = AllocatedBytes is long bytes ? $" alloc={bytes:N0}B" : string.Empty;
		var stdDev = StdDevMilliseconds is double deviation ? $" ±{deviation:F4}" : string.Empty;
		var threshold = MaxAllowedMilliseconds is double max ? $" threshold<={max:F2}ms" : string.Empty;
		var note = string.IsNullOrWhiteSpace(Note) ? string.Empty : $" note={Note}";
		return $"{Name} {status} mean={MeanMilliseconds:F4}ms{stdDev} ops/s={OperationsPerSecond:F2}{allocation}{threshold}{note}";
	}
}
