using System.Globalization;
using System.Text.Json;

namespace Purview.EventSourcing.SourceGenerator;

sealed class PerformanceHistoryStore
{
	static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

	readonly string _repositoryRoot = FindRepositoryRoot();

	string HistoryDirectory => Path.Combine(_repositoryRoot, "artifacts", "source-generator-performance", "history");

	string LatestPath => Path.Combine(_repositoryRoot, "artifacts", "source-generator-performance", "latest.json");

	public PerformanceRun? TryLoadLatest() =>
		File.Exists(LatestPath)
			? JsonSerializer.Deserialize<PerformanceRun>(File.ReadAllText(LatestPath), SerializerOptions)
			: null;

	public string Save(PerformanceRun run)
	{
		Directory.CreateDirectory(HistoryDirectory);

		var timestamp = run.TimestampUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
		var historyPath = Path.Combine(HistoryDirectory, $"{timestamp}-{run.Mode.ToUpperInvariant()}.json");
		var json = JsonSerializer.Serialize(run, SerializerOptions);

		File.WriteAllText(historyPath, json);
		File.WriteAllText(LatestPath, json);

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
