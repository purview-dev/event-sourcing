using System.Globalization;
using System.Text.Json;

namespace Purview.EventSourcing.Benchmarks;

sealed class BenchmarkHistoryStore
{
	static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

	readonly string _mode;

	readonly string _repositoryRoot = FindRepositoryRoot();

	public BenchmarkHistoryStore(string mode) => _mode = mode;

	string HistoryDirectory => Path.Combine(_repositoryRoot, "artifacts", $"{_mode}-performance", "history");

	string LatestPath => Path.Combine(_repositoryRoot, "artifacts", $"{_mode}-performance", "latest.json");

	public BenchmarkSuiteRun? TryLoadLatest() =>
		File.Exists(LatestPath)
			? JsonSerializer.Deserialize<BenchmarkSuiteRun>(File.ReadAllText(LatestPath), SerializerOptions)
			: null;

	public async Task<string> SaveAsync(BenchmarkSuiteRun run, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(HistoryDirectory);

		var timestamp = run.TimestampUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
		var historyPath = Path.Combine(HistoryDirectory, $"{timestamp}-{run.Mode.ToUpperInvariant()}.json");
		var json = JsonSerializer.Serialize(run, SerializerOptions);

		await File.WriteAllTextAsync(historyPath, json, cancellationToken);
		await File.WriteAllTextAsync(LatestPath, json, cancellationToken);

		return historyPath;
	}

	static string FindRepositoryRoot()
	{
		DirectoryInfo? current = new(Directory.GetCurrentDirectory());
		while (current is not null)
		{
			if (Directory.Exists(Path.Combine(current.FullName, ".git")))
				return current.FullName;

			current = current.Parent;
		}

		return Directory.GetCurrentDirectory();
	}
}
