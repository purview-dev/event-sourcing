using System.Globalization;
using System.Text.Json;

namespace Purview.EventSourcing.SqlServer;

sealed class SqlServerStorePerformanceHistoryStore
{
	static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

	readonly string _repositoryRoot = FindRepositoryRoot();

	string HistoryDirectory => Path.Combine(_repositoryRoot, "artifacts", "sqlserver-performance", "history");

	string LatestPath => Path.Combine(_repositoryRoot, "artifacts", "sqlserver-performance", "latest.json");

	public SqlServerStorePerformanceRun? TryLoadLatest() =>
		File.Exists(LatestPath)
			? JsonSerializer.Deserialize<SqlServerStorePerformanceRun>(File.ReadAllText(LatestPath), SerializerOptions)
			: null;

	public async Task<string> SaveAsync(SqlServerStorePerformanceRun run, CancellationToken cancellationToken)
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
