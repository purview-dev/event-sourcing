using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace Purview.EventSourcing.Postgres;

static partial class PostgresJsonIndexSchemaManager
{
	public static void ValidateOrThrow(
		PostgresJsonIndexOptions? options,
		string schemaName,
		string tableName,
		IReadOnlySet<string> supportedIncludeColumns,
		string optionsName
	)
	{
		_ = supportedIncludeColumns;
		_ = optionsName;
		_ = CreateIndexCommands(options, schemaName, tableName);
	}

	public static async Task ApplyAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction? transaction,
		string schemaName,
		string tableName,
		PostgresJsonIndexOptions? options,
		IReadOnlySet<string> supportedIncludeColumns,
		CancellationToken cancellationToken
	)
	{
		_ = supportedIncludeColumns;
		ArgumentNullException.ThrowIfNull(connection);

		foreach (var sql in CreateIndexCommands(options, schemaName, tableName))
		{
			try
			{
#pragma warning disable CA2100
				await using NpgsqlCommand command = new(sql, connection, transaction);
#pragma warning restore CA2100
				await command.ExecuteNonQueryAsync(cancellationToken);
			}
			catch (PostgresException ex) when (ex.SqlState is "42710" or "42P07")
			{
				// Cross-process duplicate create race; treat as success.
			}
		}
	}

	static List<string> CreateIndexCommands(PostgresJsonIndexOptions? options, string schemaName, string tableName)
	{
		ValidateIdentifier(schemaName, nameof(schemaName));
		ValidateIdentifier(tableName, nameof(tableName));

		if (options is null || !options.Enabled)
			return [];

		var quotedTable = $"{QuoteIdentifier(schemaName)}.{QuoteIdentifier(tableName)}";
		List<string> commands = [];

		var ginName = string.IsNullOrWhiteSpace(options.GinIndexName)
			? $"IX_{tableName}_Payload_Gin"
			: options.GinIndexName.Trim();
		ValidateIdentifier(ginName, nameof(options.GinIndexName));

		var ginOperatorClass = options.UseJsonbPathOps ? " jsonb_path_ops" : string.Empty;
		commands.Add(
			$"CREATE INDEX {QuoteIdentifier(ginName)} ON {quotedTable} USING gin ({QuoteIdentifier("Payload")}{ginOperatorClass});"
		);

		foreach (var pathIndex in options.PathIndexes)
		{
			if (pathIndex is null || string.IsNullOrWhiteSpace(pathIndex.Path))
				throw new ArgumentException("Json path index path cannot be null or empty.", nameof(options));

			var tokens = ParsePath(pathIndex.Path);
			if (tokens.Length == 0)
				throw new ArgumentException(
					$"Json path '{pathIndex.Path}' did not produce any path tokens.",
					nameof(options)
				);

			var expression = $"({QuoteIdentifier("Payload")} #>> '{CreatePgTextPath(tokens)}')";
			var indexName = string.IsNullOrWhiteSpace(pathIndex.IndexName)
				? $"IX_{tableName}_{CreateStableHash(pathIndex.Path)}"
				: pathIndex.IndexName.Trim();
			ValidateIdentifier(indexName, nameof(pathIndex.IndexName));

			commands.Add($"CREATE INDEX {QuoteIdentifier(indexName)} ON {quotedTable} ({expression});");
		}

		return commands;
	}

	static string[] ParsePath(string path)
	{
		var normalized = path.Trim();
		if (normalized.StartsWith("$.", StringComparison.Ordinal))
			normalized = normalized[2..];
		else if (normalized.StartsWith('$', StringComparison.Ordinal))
			normalized = normalized[1..];

		return normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	static string CreatePgTextPath(IEnumerable<string> tokens) =>
		"{" + string.Join(",", tokens.Select(EscapePgPathToken)) + "}";

	static string EscapePgPathToken(string token) =>
		token.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

	static string CreateStableHash(string value)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
		return Convert.ToHexString(hash[..6]);
	}

	static void ValidateIdentifier(string identifier, string parameterName)
	{
		if (string.IsNullOrWhiteSpace(identifier))
			throw new ArgumentException("Identifier cannot be null or empty.", parameterName);

		if (!IdentifierRegex().IsMatch(identifier))
			throw new ArgumentException($"Identifier '{identifier}' contains invalid characters.", parameterName);
	}

	static string QuoteIdentifier(string identifier) => $"\"{identifier}\"";

	[GeneratedRegex(@"^[\w\-\.]+$")]
	private static partial Regex IdentifierRegex();
}
