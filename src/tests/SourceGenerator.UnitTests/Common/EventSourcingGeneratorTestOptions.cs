namespace Purview.EventSourcing.SourceGenerator.Common;

public record EventSourcingGeneratorTestOptions : SourceGeneratorTestOptions
{
	public const string PreCompilationMarkerHintName = "PreCompilationMarker.g.cs";

	public static readonly string[] AggregateGeneratedAttributes =
	[
		"EmbeddedAttribute.g.cs",
		"AggregateAttributes.g.cs",
	];

	public static readonly int AggregateExpectedFileCount = AggregateGeneratedAttributes.Length + 1;

	public static readonly int AggregateExpectedFileCountPlusGen = AggregateExpectedFileCount + 1;

	public const int HintNameHashHexLength = 16;

	public const string GeneratedSourceFileSuffix = ".g.cs";

	public EventSourcingGeneratorTestOptions()
	{
		DisableSourceGeneratorPropertyName = PropertyLibrary.DisableSourceGenerator;
		ValidateCodeWriterScopes = true;
		AdditionalNamespaces =
		[
			typeof(EventStoreSet<>).Namespace!,
			typeof(Aggregates.AggregateBase).Namespace!,
			typeof(ValueObjects.Serialization.ScalarJsonConverterFactory).Namespace!,
			typeof(ValueObjects.IValueObject).Namespace!,
		];
		AdditionalAssemblyTypes = [typeof(Aggregates.IAggregate), typeof(ValueObjects.IValueObject)];
		AdditionalReferences = [.. TestMetadataReferences.GetAdditionalReferences()];
		ExcludeGeneratedSourceHintNames = [.. AggregateGeneratedAttributes, PreCompilationMarkerHintName];
		AnalyzerTypes = [typeof(Analyzers.AggregateDiagnosticAnalyzer)];
	}

	public static new EventSourcingGeneratorTestOptions Default => new();

	public static readonly EventSourcingGeneratorTestOptions NoValidation = new()
	{
		ThrowOnGenerationException = false,
	};
}
