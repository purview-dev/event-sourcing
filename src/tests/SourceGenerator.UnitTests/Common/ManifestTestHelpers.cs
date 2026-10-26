using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Purview.EventSourcing.SourceGenerator.Common;

/// <summary>
/// Helpers for exercising the event-contract manifest from generator tests.
/// </summary>
static class ManifestTestHelpers
{
	public const string ManifestEnabledAnalyzerOption = "build_property.PurviewEventContractManifestEnabled";

	public const string BaselineFileName = "EventContractManifest.json";

	public static EventSourcingGeneratorTestOptions WithManifestEnabled() =>
		new()
		{
			AnalyzerConfigOptions = ImmutableDictionary<string, string>.Empty.Add(
				ManifestEnabledAnalyzerOption,
				"true"
			),
		};

	public static EventSourcingGeneratorTestOptions WithManifestEnabledAndNullableContext() =>
		new()
		{
			AnalyzerConfigOptions = ImmutableDictionary<string, string>.Empty.Add(
				ManifestEnabledAnalyzerOption,
				"true"
			),
			NullableContextOptions = NullableContextOptions.Enable,
		};

	public static EventSourcingGeneratorTestOptions WithBaseline(string baselineJson) =>
		new() { AdditionalText = [new InMemoryAdditionalText(BaselineFileName, baselineJson)] };

	public static EventSourcingGeneratorTestOptions WithBaselineAndNullableContext(string baselineJson) =>
		new()
		{
			AdditionalText = [new InMemoryAdditionalText(BaselineFileName, baselineJson)],
			NullableContextOptions = NullableContextOptions.Enable,
		};

	/// <summary>
	/// Extracts the JSON event-contract manifest from the generated <c>EventContractManifest.g.cs</c>
	/// source, mirroring the shipped MSBuild extraction target.
	/// </summary>
	public static string ExtractManifestJson(string generatedSource)
	{
		const string marker = "public const string Json =";
		var markerIndex = generatedSource.IndexOf(marker, StringComparison.Ordinal);
		if (markerIndex < 0)
			throw new InvalidOperationException($"Generated source does not contain '{marker}'.");

		var valueStart = generatedSource.IndexOf('"', markerIndex, StringComparison.Ordinal) + 1;
		var valueEnd = FindClosingQuote(generatedSource, valueStart);
		if (valueStart <= markerIndex || valueEnd <= valueStart)
			throw new InvalidOperationException("Generated manifest constant is malformed.");

		var literal = generatedSource[valueStart..valueEnd];
		return Unescape(literal);
	}

	static int FindClosingQuote(string text, int start)
	{
		for (var i = start; i < text.Length; i++)
		{
			if (text[i] == '\\')
			{
				i++;
				continue;
			}

			if (text[i] == '"')
				return i;
		}

		return -1;
	}

	public static string GetManifestSource(DriverRunResult result)
	{
		foreach (var generatedSource in result.DriverResult.Results[0].GeneratedSources)
		{
			if (StringComparer.Ordinal.Equals(generatedSource.HintName, EventContractManifestLibrary.GeneratedHintName))
				return generatedSource.SourceText.ToString();
		}

		return string.Empty;
	}

	static string Unescape(string literal)
	{
		StringBuilder builder = new(literal.Length);
		for (var i = 0; i < literal.Length; i++)
		{
			if (literal[i] != '\\' || i + 1 >= literal.Length)
			{
				builder.Append(literal[i]);
				continue;
			}

			var next = literal[i + 1];
			if (next is '\\' or '"')
			{
				builder.Append(next);
				i++;
			}
			else
			{
				builder.Append(literal[i]);
			}
		}

		return builder.ToString();
	}
}
